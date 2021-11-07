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
			if (FormatsFetch && xevent.SelectionEvent.target == TARGETS) {
Console.Out.WriteLine("X11Clipboard.HandleSelectionNotifyEvent TARGETS");
				if (xevent.SelectionEvent.property != IntPtr.Zero) {
					var fake_dnd = new XClientMessageEvent();
					fake_dnd.ptr2 = (IntPtr) 1; // use window property

					Formats = X11SelectionHandler.TypeListConvert(xevent.AnyEvent.display,
						    xevent.AnyEvent.window,
						    xevent.SelectionEvent.property, ref fake_dnd);
				}
				FormatsFetch = false;
			} else {
Console.Out.WriteLine("X11Clipboard.HandleSelectionNotifyEvent Content");
				base.HandleSelectionNotifyEvent (ref xevent);
			}
		}

		internal string[] GetFormats () {
			if (Outgoing != null) {
Console.Out.WriteLine("X11Clipboard.GetFormats - MONO");
				// from mono - to mono
				return Outgoing.GetFormats();
			}

Console.Out.WriteLine("X11Clipboard.GetFormats - native");
			FormatsFetch = true;
			Formats = null;

			if (1 != XplatUIX11.XConvertSelection(XplatUIX11.Display, Selection, TARGETS, TARGETS, FosterParent, IntPtr.Zero))
				return new string[0];

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
			XplatUIX11.XSetSelectionOwner (XplatUIX11.Display, Selection, IntPtr.Zero, IntPtr.Zero);

			// to avoid race-conditions with handleSelectionClearEvent
			Outgoing = null;
		}

		internal IDataObject GetContent () {
Console.Out.WriteLine("X11Clipboard.GetContent");
			if (Outgoing != null) {
				// from mono - to mono
				return Outgoing;
			}

			Incomming = new DataObject();

			var owner = XplatUIX11.XGetSelectionOwner (XplatUIX11.Display, Selection);
			if (owner == IntPtr.Zero)
				return Incomming;

			var fake_dnd = new XClientMessageEvent();
			fake_dnd.ptr2 = (IntPtr) 1; // use window property

			var handlers = X11SelectionHandler.TypeListHandlers(XplatUIX11.Display, owner, TARGETS, ref fake_dnd);

			foreach (var handler in handlers){
				Console.Out.WriteLine(handler);/*
				// TODO locking
				if (X11SelectionHandler.ConvertSelectionClipboard(XplatUIX11.Display, Selection, FosterParent)){
					ConvertPending++
				}*/
			}
			// TODO wait
			return Incomming;
		}

		internal void SetContent (object data, bool copy) {
Console.Out.WriteLine($"X11Clipboard.SetContent {data.GetType().FullName} {copy}");
			var iData = data as IDataObject;
			if (iData == null) {
				iData = new DataObject();
				X11SelectionHandler.SetDataWithFormats (iData, data);
			}

			Outgoing = iData;
			XplatUIX11.XSetSelectionOwner (XplatUIX11.Display, Selection, FosterParent, IntPtr.Zero);

			if (copy){
				// TODO
				throw new NotImplementedException("permanent copy to GTK not jet implemented");
			}
		}
	}
}

