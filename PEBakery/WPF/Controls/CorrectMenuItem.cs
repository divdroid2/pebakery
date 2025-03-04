﻿/*
    MIT License (MIT)

    Copyright (C) 2018-2022 Hajin Jang
	
	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:
	
	The above copyright notice and this permission notice shall be included in all
	copies or substantial portions of the Software.
	
	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
	SOFTWARE.
*/

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PEBakery.WPF.Controls
{
    /// <summary>
    /// MenuItem rendered properly in both Win 10 Aero and Win 7 Classic.
    /// </summary>
    public class CorrectMenuItem : MenuItem
    {
        public CorrectMenuItem()
        {
            if (Icon is not FrameworkElement iconElement)
                return;
            if (VisualTreeHelper.GetParent(iconElement) is not ContentPresenter presenter)
                return;

            const int presenterMargin = 2;
            double maxMargin = presenterMargin;
            Thickness margin = iconElement.Margin;
            if (maxMargin < margin.Top)
                maxMargin = margin.Top;
            else if (maxMargin < margin.Bottom)
                maxMargin = margin.Bottom;
            else if (maxMargin < margin.Left)
                maxMargin = margin.Left;
            else if (maxMargin < margin.Right)
                maxMargin = margin.Right;

            iconElement.Width -= maxMargin;
            iconElement.Height -= maxMargin;
            iconElement.HorizontalAlignment = HorizontalAlignment.Center;
            iconElement.VerticalAlignment = VerticalAlignment.Center;
            presenter.Margin = new Thickness(presenterMargin);
        }
    }
}
