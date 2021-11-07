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
		readonly UpdateMessageQueueDG UpdateMessageQueue;
		readonly TimeSpan TimeToWaitForSelectionFormats;
		
		bool Enumerating;		// value is zero if we are querying available formats

		internal X11Clipboard (string selection, UpdateMessageQueueDG updateMessageQueue)
			: base (selection)
		{
			UpdateMessageQueue = updateMessageQueue;


			TARGETS = XplatUIX11.XInternAtom (XplatUIX11.Display, "TARGETS", false);

			TimeToWaitForSelectionFormats = TimeSpan.FromSeconds(4);
		}

		internal override void HandleSelectionNotifyEvent (ref XEvent xevent)
		{
Console.Out.WriteLine($"X11Clipboard.HandleSelectionNotifyEvent {xevent}");
			base.HandleSelectionNotifyEvent (ref xevent);

			// we requested something the source right now doesn't support
			if (Enumerating) {
				if (xevent.SelectionEvent.property == IntPtr.Zero) {
					Content = null;
				}
				Enumerating = false;
			}
		}

		internal override void HandleSelectionClearEvent (ref XEvent xevent) {
Console.Out.WriteLine($"X11Clipboard.HandleSelectionClearEvent {xevent}");
			base.HandleSelectionClearEvent (ref xevent);
			Content = null;
		}

		internal string[] GetFormats () {
Console.Out.WriteLine("X11Clipboard.GetFormats");
			if (XplatUIX11.XGetSelectionOwner(XplatUIX11.Display, Selection) == IntPtr.Zero) {
				return new string[0];
			}

			// TARGETS is supported by all, no iteration required - see ICCCM chapter 2.6.2. Target Atoms
			Enumerating = true;

			XplatUIX11.XConvertSelection(XplatUIX11.Display, Selection, TARGETS, TARGETS, XplatUIX11.RootWindowHandle, IntPtr.Zero);

			var startTime = DateTime.UtcNow;
			while (Enumerating) {
				UpdateMessageQueue(null, false);

				if (DateTime.UtcNow - startTime > TimeToWaitForSelectionFormats)
					break;
			}

			return Content?.GetFormats();
		}
		
		internal void Clear () {
Console.Out.WriteLine("X11Clipboard.Clear");
			Content = null;
			XplatUIX11.XSetSelectionOwner (XplatUIX11.Display, Selection, XplatUIX11.RootWindowHandle, IntPtr.Zero);
		}

		internal IDataObject GetContent () {
Console.Out.WriteLine("X11Clipboard.GetContent");
			throw new NotImplementedException();
		}

		internal void SetContent (object data, bool copy) {
Console.Out.WriteLine("X11Clipboard.SetContent");
			throw new NotImplementedException();
		}
	}
}

