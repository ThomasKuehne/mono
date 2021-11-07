// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (c) 2004-2006 Novell, Inc.
// Copyright (c) 2009 Novell, Inc.
// Copyright (c) 2021 Thomas Kuehne
//
// Authors:
//	Peter Bartok	pbartok@novell.com
//	Carlos Alberto Cortez (calberto.cortez@gmail.com)
//	Thomas Kuehne	thomas@kuehne.cn
//

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Text;
using System;

namespace System.Windows.Forms {

	internal class X11Clipboard : X11Selection {
		internal delegate void UpdateMessageQueueDG (XEventQueue queue, bool allowIdle);

		readonly IntPtr TARGETS;
		readonly IntPtr FosterParent;
		readonly UpdateMessageQueueDG UpdateMessageQueue;
		readonly TimeSpan TimeToWaitForSelectionFormats;
		
		bool FormatsFetch;		// value is zero if we are querying available formats
		string[] Formats;

		internal X11Clipboard (string selection, UpdateMessageQueueDG updateMessageQueue, IntPtr fosterParent)
			: base (selection)
		{
			UpdateMessageQueue = updateMessageQueue;
			FosterParent = fosterParent;

			TARGETS = XplatUIX11.XInternAtom (XplatUIX11.Display, "TARGETS", false);

			TimeToWaitForSelectionFormats = TimeSpan.FromSeconds(4);
		}

		internal override void HandleSelectionNotifyEvent (ref XEvent xevent)
		{
Console.Out.WriteLine($"X11Clipboard.HandleSelectionNotifyEvent {xevent}");
			if (FormatsFetch && xevent.SelectionEvent.target == TARGETS) {
				if (xevent.SelectionEvent.property != IntPtr.Zero) {
					Formats = X11SelectionHandler.ConvertTypeList(xevent.AnyEvent.display,
						    xevent.AnyEvent.window,
						    xevent.SelectionEvent.property);
				}
				FormatsFetch = false;
			} else {
				base.HandleSelectionNotifyEvent (ref xevent);
			}
		}

		internal override void HandleSelectionClearEvent (ref XEvent xevent) {
Console.Out.WriteLine($"X11Clipboard.HandleSelectionClearEvent {xevent}");
			base.HandleSelectionClearEvent (ref xevent);
			Content = null;
		}

		internal string[] GetFormats () {
Console.Out.WriteLine("X11Clipboard.GetFormats");
			FormatsFetch = true;
			Formats = null;

			XplatUIX11.XConvertSelection(XplatUIX11.Display, Selection, TARGETS, TARGETS, FosterParent, IntPtr.Zero);

			var startTime = DateTime.UtcNow;
			while (FormatsFetch) {
				UpdateMessageQueue(null, false);

				if (DateTime.UtcNow - startTime > TimeToWaitForSelectionFormats)
					break;
			}

			return Formats ?? new string[0];
		}
		
		internal void Clear () {
Console.Out.WriteLine("X11Clipboard.Clear");
			Content = null;
			XplatUIX11.XSetSelectionOwner (XplatUIX11.Display, Selection, IntPtr.Zero, IntPtr.Zero);
		}

		internal IDataObject GetContent () {
Console.Out.WriteLine("X11Clipboard.GetContent");
			if (Content != null)
				return Content;

			// TODO
			return null;
		}

		internal void SetContent (object data, bool copy) {
Console.Out.WriteLine($"X11Clipboard.SetContent {data} {copy}");
			var iData = data as IDataObject;
			if (data != null && iData == null) {
				iData = new DataObject();
				X11SelectionHandler.SetDataWithFormats (iData, data);
			}

			Content = iData;
			XplatUIX11.XSetSelectionOwner (XplatUIX11.Display, Selection, FosterParent, IntPtr.Zero);

			if (copy){
				// TODO
			}
		}
	}
}

