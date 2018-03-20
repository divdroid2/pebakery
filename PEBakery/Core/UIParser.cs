﻿/*
    Copyright (C) 2016-2018 Hajin Jang
    Licensed under GPL 3.0
 
    PEBakery is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.

    Additional permission under GNU GPL version 3 section 7

    If you modify this program, or any covered work, by linking
    or combining it with external libraries, containing parts
    covered by the terms of various license, the licensors of
    this program grant you additional permission to convey the
    resulting work. An external library is a library which is
    not derived from or based on this program. 
*/

using PEBakery.Exceptions;
using PEBakery.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace PEBakery.Core
{
    public static class UIParser
    {
        public static List<UIControl> ParseRawLines(List<string> lines, SectionAddress addr, out List<LogInfo> errorLogs)
        {
            // Select Code sections and compile
            errorLogs = new List<LogInfo>();
            List<UIControl> uiCtrls = new List<UIControl>();
            for (int i = 0; i < lines.Count; i++)
            {
                try
                {
                    UIControl uiCtrl = ParseUIControl(lines, addr, ref i);
                    
                    if (uiCtrl != null)
                    {
                        // Check if interface control's key is duplicated
                        if (uiCtrls.Count(x => x.Key.Equals(uiCtrl.Key, StringComparison.OrdinalIgnoreCase)) == 0)
                            uiCtrls.Add(ParseUIControl(lines, addr, ref i));
                        else
                            errorLogs.Add(new LogInfo(LogState.Error, $"Interface key [{uiCtrl.Key}] is duplicated ({uiCtrl.RawLine})"));
                    }
                }
                catch (InvalidUIControlException e)
                {
                    errorLogs.Add(new LogInfo(LogState.Error, $"{Logger.LogExceptionMessage(e)} ({e.UICtrl.RawLine})"));
                }
                catch (InvalidCommandException e)
                {
                    errorLogs.Add(new LogInfo(LogState.Error, $"{Logger.LogExceptionMessage(e)} ({e.RawLine})"));
                }
                catch (Exception e)
                {
                    errorLogs.Add(new LogInfo(LogState.Error, e));
                }
            }

            return uiCtrls.Where(x => x.Type != UIControlType.None).ToList();
        }

        public static UIControl ParseUIControl(List<string> rawLines, SectionAddress addr, ref int idx)
        {
            UIControlType type;
            string rawLine = rawLines[idx].Trim();

            // Check if rawCode is Empty
            if (rawLine.Equals(string.Empty))
                return null;

            // Comment Format : starts with '//' or '#', ';'
            if (rawLine.StartsWith("//") || rawLine.StartsWith("#") || rawLine.StartsWith(";"))
                return null;

            // Find key of interface control
            string key;
            string rawValue = string.Empty;
            int equalIdx = rawLine.IndexOf('=');
            if (equalIdx != -1 && equalIdx != 0)
            {
                key = rawLine.Substring(0, equalIdx);
                rawValue = rawLine.Substring(equalIdx + 1);
            }
            else
            {
                throw new InvalidCommandException($"Interface control [{rawValue}] does not have a name defined", rawLine);
            }

            // Parse Arguments
            List<string> args = new List<string>();
            try
            {
                string remainder = rawValue;
                while (remainder != null)
                {
                    Tuple<string, string> tuple = CodeParser.GetNextArgument(remainder);
                    args.Add(tuple.Item1);
                    remainder = tuple.Item2;
                }
            }
            catch (InvalidCommandException e) { throw new InvalidCommandException(e.Message, rawLine); }

            // Check doublequote's occurence - must be 2n
            if (StringHelper.CountOccurrences(rawValue, "\"") % 2 == 1)
                throw new InvalidCommandException($"Interface control [{rawValue}]'s doublequotes mismatch", rawLine);

            // Check if last operand is \ - MultiLine check - only if one or more operands exists
            if (0 < args.Count)
            {
                while (args.Last().Equals(@"\", StringComparison.OrdinalIgnoreCase))
                { // Split next line and append to List<string> operands
                    if (rawLines.Count <= idx) // Section ended with \, invalid grammar!
                        throw new InvalidCommandException($@"Last interface control [{rawValue}] cannot end with '\' ", rawLine);
                    idx++;
                    args.AddRange(rawLines[idx].Trim().Split(','));
                }
            }

            // UIControl should have at least 7 operands
            //    Text, Visibility, Type, X, Y, width, height, [Optional]
            if (args.Count < 7)
                throw new InvalidCommandException($"Interface control [{rawValue}] must have at least 7 arguments", rawLine);

            // Parse opcode
            try { type = UIParser.ParseControlType(args[2]); }
            catch (InvalidCommandException e) { throw new InvalidCommandException(e.Message, rawLine); }

            // Remove UIControlType from operands
            //   Leftover : Text, Visibility, X, Y, width, height, [Optional]
            args.RemoveAt(2);

            // Forge UIControl
            string text = StringEscaper.Unescape(args[0]);
            string visibilityStr = args[1];
            bool visibility;
            if (visibilityStr.Equals("1", StringComparison.Ordinal) ||
                visibilityStr.Equals("True", StringComparison.OrdinalIgnoreCase))
                visibility = true;
            else if (visibilityStr.Equals("0", StringComparison.Ordinal) ||
                     visibilityStr.Equals("False", StringComparison.OrdinalIgnoreCase))
                visibility = false;
            else
                throw new InvalidCommandException($"Invalid value in [{visibilityStr}]", rawLine);

            bool intParse = true;
            intParse &= NumberHelper.ParseInt32(args[2], out int x);
            intParse &= NumberHelper.ParseInt32(args[3], out int y);
            intParse &= NumberHelper.ParseInt32(args[4], out int width);
            intParse &= NumberHelper.ParseInt32(args[5], out int height);
            if (intParse == false)
                throw new InvalidCommandException($"Invalid integers in [{rawValue}]", rawLine);

            Rect rect = new Rect(x, y, width, height);
            UIInfo info;
            try { info = ParseUIControlInfo(type, args); }
            catch (InvalidCommandException e) { throw new InvalidCommandException(e.Message, rawLine); }
            return new UIControl(rawLine, addr, key, text, visibility, type, rect, info);
        }

        public static UIControlType ParseControlType(string typeStr)
        {
            // typeStr must be number
            if (!StringHelper.IsInteger(typeStr))
                throw new InvalidCommandException("Only numbers can be used for interface control type");

            if (!(Enum.TryParse(typeStr, false, out UIControlType type) && Enum.IsDefined(typeof(UIControlType), type)))
                throw new InvalidCommandException("Invalid interface control type");

            return type;
        }

        private static UIInfo ParseUIControlInfo(UIControlType type, List<string> fullArgs)
        {
            // Only use fields starting from 8th operand
            List<string> args = fullArgs.Skip(6).ToList(); // Remove Text, Visibility, X, Y, width, height

            switch (type)
            {
                #region TextBox
                case UIControlType.TextBox:
                    {
                        const int minOpCount = 1;
                        const int maxOpCount = 1;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1)) // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        return new UIInfo_TextBox(GetInfoTooltip(args, maxOpCount), StringEscaper.Unescape(args[0]));
                    }
                #endregion
                #region TextLabel
                case UIControlType.TextLabel:
                    {
                        const int minOpCount = 1;
                        const int maxOpCount = 2;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1)) // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        if (!NumberHelper.ParseInt32(args[0], out int fontSize))
                            throw new InvalidCommandException($"FontSize {args[0]} is not a valid integer");

                        UIInfo_TextLabel_Style style = UIInfo_TextLabel_Style.Normal;
                        if (args[1].Equals("Bold", StringComparison.OrdinalIgnoreCase))
                            style = UIInfo_TextLabel_Style.Bold;
                        else if (args[1].Equals("Italic", StringComparison.OrdinalIgnoreCase))
                            style = UIInfo_TextLabel_Style.Italic;
                        else if (args[1].Equals("Underline", StringComparison.OrdinalIgnoreCase))
                            style = UIInfo_TextLabel_Style.Underline;
                        else if (args[1].Equals("Strike", StringComparison.OrdinalIgnoreCase))
                            style = UIInfo_TextLabel_Style.Strike;

                        return new UIInfo_TextLabel(GetInfoTooltip(args, maxOpCount), fontSize, style);
                    }
                #endregion
                #region NumberBox
                case UIControlType.NumberBox:
                    {
                        const int minOpCount = 4;
                        const int maxOpCount = 4;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1)) // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        NumberHelper.ParseInt32(args[0], out int value);
                        NumberHelper.ParseInt32(args[1], out int min);
                        NumberHelper.ParseInt32(args[2], out int max);
                        NumberHelper.ParseInt32(args[3], out int interval);

                        return new UIInfo_NumberBox(GetInfoTooltip(args, maxOpCount), value, min, max, interval);
                    }
                #endregion
                #region CheckBox
                case UIControlType.CheckBox:
                    {
                        const int minOpCount = 1;
                        const int maxOpCount = 3; // +2 for [RunOptional]
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1)) // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        bool _checked = false;
                        if (args[0].Equals("True", StringComparison.OrdinalIgnoreCase))
                            _checked = true;
                        else if (args[0].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                            throw new InvalidCommandException($"Invalid argument [{args[0]}], must be [True] or [False]");

                        string tooltip = null;
                        if (args.Last().StartsWith("__", StringComparison.Ordinal)) // Has <ToolTip>
                            tooltip = GetInfoTooltip(args, args.Count - 1);

                        string sectionName = null;
                        bool hideProgress = false;
                        if (3 <= args.Count &&
                            (args[2].Equals("True", StringComparison.OrdinalIgnoreCase) || args[2].Equals("False", StringComparison.OrdinalIgnoreCase)) &&
                            args[1].StartsWith("_", StringComparison.Ordinal) &&
                            args[1].EndsWith("_", StringComparison.Ordinal))
                        { // Has [RunOptinal] -> <SectionName>,<HideProgress>
                            if (args[2].Equals("True", StringComparison.OrdinalIgnoreCase))
                                hideProgress = true;
                            else if (args[2].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                                throw new InvalidCommandException($"Invalid argument [{args[2]}], must be [True] or [False]");

                            sectionName = args[1].Substring(1, args[1].Length - 2);
                        }

                        return new UIInfo_CheckBox(tooltip, _checked, sectionName, hideProgress);
                    }
                #endregion
                #region ComboBox
                case UIControlType.ComboBox:
                    { // Variable Length
                        List<string> items = new List<string>();

                        // Have ToolTip?
                        string toolTip = null;
                        int cnt = args.Count;
                        if (args.Last().StartsWith("__", StringComparison.Ordinal))
                        {
                            toolTip = args.Last();
                            cnt -= 1;
                        }

                        string sectionName = null;
                        bool hideProgress = false;
                        if (2 <= cnt &&
                            (args[cnt - 1].Equals("True", StringComparison.OrdinalIgnoreCase) || args[cnt - 1].Equals("False", StringComparison.OrdinalIgnoreCase)) &&
                            args[cnt - 2].StartsWith("_", StringComparison.Ordinal) &&
                            args[cnt - 2].EndsWith("_", StringComparison.Ordinal))
                        { // Has [RunOptinal] -> <SectionName>,<HideProgress>
                            if (args[cnt - 1].Equals("True", StringComparison.OrdinalIgnoreCase))
                                hideProgress = true;
                            else if (args[cnt - 1].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                                throw new InvalidCommandException($"Invalid argument [{args[cnt - 1]}], must be [True] or [False]");

                            sectionName = args[cnt - 2].Substring(1, args[cnt - 2].Length - 2);
                            cnt -= 2;
                        }

                        for (int i = 0; i < cnt; i++)
                            items.Add(args[i]);

                        int idx = items.IndexOf(fullArgs[0]);
                        if (idx == -1) 
                            throw new InvalidCommandException($"[{type}] has wrong selected value [{fullArgs[0]}]");

                        return new UIInfo_ComboBox(toolTip, items, idx, sectionName, hideProgress);
                    }
                #endregion
                #region Image
                case UIControlType.Image:
                    {
                        const int minOpCount = 0;
                        const int maxOpCount = 1; // [URL]
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1))  // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        string url = null;
                        if (1 <= args.Count)
                            url = args[0];

                        return new UIInfo_Image(GetInfoTooltip(args, maxOpCount), url);
                    }
                #endregion
                #region TextFile
                case UIControlType.TextFile:
                    {
                        const int minOpCount = 0;
                        const int maxOpCount = 0;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1))
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        return new UIInfo_TextFile(GetInfoTooltip(args, maxOpCount));
                    }
                #endregion
                #region Button
                case UIControlType.Button:
                    { // <SectionToRun>,<Picture>,[HideProgress]  +[UnknownBoolean] +[RunOptional]
                        // Ex)
                        // pButton1 =,1,8,382,47,24,24,Process-OpenDriver_x86,opendir.bmp,False,_Process-OpenDriver_x86,False,_Process-OpenDriver_x86_,False
                        // Button_Download=,1,8,403,21,24,24,DownloadXXX,DoubleJDesignRavenna3dArrowDown0016016.bmp,False,False,_DownloadXXX_,False,"__DOWNLOAD Script"
                        // OpendirSMFilesButton=,1,8,475,204,24,24,Opendir_SMFiles,opendir.bmp,"__Open Custom .ini Folder"
                        // Button_HiveUnload_Target="HiveUnload: Target + ProjectTemp + MountFolders",1,8,15,17,293,46,HiveUnload_Launch_B,HiveUnload3232.bmp,0,"__UnLoad hives"
                        // Button_Tools_Folder="Open Tools Folder",1,8,98,256,134,25,Open_Tools_Folder
                        const int minOpCount = 1;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, -1))
                            throw new InvalidCommandException($"[{type}] must have at least [{minOpCount}] arguments");

                        int cnt = args.Count;
                        string tooltip = null;
                        if (args.Last().StartsWith("__", StringComparison.Ordinal)) // Has <ToolTip>
                        {
                            tooltip = GetInfoTooltip(args, cnt - 1);
                            cnt -= 1;
                        }

                        string picture = null;
                        if (2 <= cnt)
                        {
                            if (args[1].Equals("0", StringComparison.OrdinalIgnoreCase) == false)
                                picture = args[1];
                        }

                        bool hideProgress = false;
                        if (3 <= cnt)
                        {
                            if (args[2].Equals("True", StringComparison.OrdinalIgnoreCase))
                                hideProgress = true;
                            else if (args[2].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                            {
                                // WB082 Compability Shim
                                if (args[2].Equals("1", StringComparison.Ordinal))
                                    hideProgress = true;
                                else if (args[2].Equals("0", StringComparison.Ordinal) == false)
                                    throw new InvalidCommandException($"Invalid argument [{args[2]}], must be [True] or [False]");
                            }
                        }

                        // Ignore [UnknownBoolean] and [RunOptional]
                        return new UIInfo_Button(tooltip, args[0], picture, hideProgress);
                    }
                #endregion
                #region WebLabel
                case UIControlType.WebLabel:
                    {
                        const int minOpCount = 1;
                        const int maxOpCount = 1;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1)) // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        return new UIInfo_WebLabel(GetInfoTooltip(args, maxOpCount), StringEscaper.Unescape(args[0]));
                    }
                #endregion
                #region RadioButton
                case UIControlType.RadioButton:
                    {
                        const int minOpCount = 1;
                        const int maxOpCount = 3; // +2 for [RunOptional]
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1))
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        bool selected = false;
                        if (args[0].Equals("True", StringComparison.OrdinalIgnoreCase))
                            selected = true;
                        else if (args[0].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                            throw new InvalidCommandException($"Invalid argument [{args[0]}], must be [True] or [False]");

                        string tooltip = null;
                        if (args.Last().StartsWith("__", StringComparison.Ordinal)) // Has <ToolTip>
                            tooltip = GetInfoTooltip(args, args.Count - 1);

                        string sectionName = null;
                        bool hideProgress = false;
                        if (3 <= args.Count &&
                            (args[2].Equals("True", StringComparison.OrdinalIgnoreCase) || args[2].Equals("False", StringComparison.OrdinalIgnoreCase)) &&
                            args[1].StartsWith("_", StringComparison.Ordinal) &&
                            args[1].EndsWith("_", StringComparison.Ordinal))
                        { // Has [RunOptinal] -> <SectionName>,<HideProgress>
                            if (args[2].Equals("True", StringComparison.OrdinalIgnoreCase))
                                hideProgress = true;
                            else if (args[2].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                                throw new InvalidCommandException($"Invalid argument [{args[2]}], must be [True] or [False]");

                            sectionName = args[1].Substring(1, args[1].Length - 2);
                        }

                        return new UIInfo_RadioButton(tooltip, selected, sectionName, hideProgress);
                    }
                #endregion
                #region Bevel
                case UIControlType.Bevel:
                    {
                        const int minOpCount = 0;
                        const int maxOpCount = 2;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1)) // +1 for tooltip
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        int? fontSize = null;
                        UIInfo_BevelCaption_Style? style = null;

                        if (1 <= args.Count)
                        {
                            if (!NumberHelper.ParseInt32(args[0], out int fontSizeVal))
                                throw new InvalidCommandException($"FontSize {args[0]} is not a valid integer");
                            fontSize = fontSizeVal;
                        }
                            
                        if (2 <= args.Count)
                        {
                            if (args[1].Equals("Normal", StringComparison.OrdinalIgnoreCase))
                                style = UIInfo_BevelCaption_Style.Normal;
                            else if (args[1].Equals("Bold", StringComparison.OrdinalIgnoreCase))
                                style = UIInfo_BevelCaption_Style.Bold;
                        }

                        return new UIInfo_Bevel(GetInfoTooltip(args, maxOpCount), fontSize, style);
                    }
                #endregion
                #region FileBox
                case UIControlType.FileBox:
                    {
                        const int minOpCount = 0;
                        const int maxOpCount = 1;
                        if (CodeParser.CheckInfoArgumentCount(args, minOpCount, maxOpCount + 1))
                            throw new InvalidCommandException($"[{type}] can have [{minOpCount}] ~ [{maxOpCount + 1}] arguments");

                        bool isFile = false;
                        if (0 < args.Count)
                        {
                            if (args[0].Equals("file", StringComparison.OrdinalIgnoreCase))
                                isFile = true;
                            else if (!args[0].Equals("dir", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidCommandException($"Argument [{type}] should be either [file] or [dir]");
                        }

                        return new UIInfo_FileBox(GetInfoTooltip(args, maxOpCount), isFile);
                    }
                #endregion
                #region RadioGroup
                case UIControlType.RadioGroup:
                    { // Variable Length
                        List<string> items = new List<string>();

                        string sectionName = null;
                        bool showProgress = false;

                        int cnt = args.Count - 1;
                        if (args.Last().StartsWith("__", StringComparison.Ordinal)) // Has <ToolTip>
                            cnt -= 1;

                        if ((args[cnt].Equals("True", StringComparison.OrdinalIgnoreCase) || args[cnt].Equals("False", StringComparison.OrdinalIgnoreCase)) && 
                            args[cnt - 1].StartsWith("_", StringComparison.Ordinal) &&
                            args[cnt - 1].EndsWith("_", StringComparison.Ordinal))
                        { // Has [RunOptinal] -> <SectionName>,<HideProgress>
                            if (args[cnt].Equals("True", StringComparison.OrdinalIgnoreCase))
                                showProgress = true;
                            else if (args[cnt].Equals("False", StringComparison.OrdinalIgnoreCase) == false)
                                throw new InvalidCommandException($"Invalid argument [{args[cnt]}], must be [True] or [False]");

                            sectionName = args[cnt - 1].Substring(1, args[cnt - 1].Length - 2);

                            cnt -= 2;
                        }

                        for (int i = 0; i < cnt; i++)
                            items.Add(args[i]);
                        
                        if (NumberHelper.ParseInt32(args[cnt], out int idx) == false)
                            throw new InvalidCommandException($"Invalid argument [{args[cnt]}], must be an integer");

                        return new UIInfo_RadioGroup(GetInfoTooltip(args, args.Count), items, idx, sectionName, showProgress);
                    }
                #endregion
                #region default
                default:
                    Debug.Assert(false);
                    break;
                #endregion
            }

            throw new InvalidCommandException($"Invalid interface control type [{type}]");
        }

        /// <summary>
        /// Extract tooltip from operand
        /// </summary>
        /// <param name="op"></param>
        /// <param name="idx">Tooltip operator's operand index</param>
        /// <returns></returns>
        private static string GetInfoTooltip(List<string> op, int idx)
        {
            if (idx < op.Count && op[idx].StartsWith("__", StringComparison.Ordinal)) // Has tooltip
                return op[idx].Substring(2);
            else
                return null;
        }
    }
}
