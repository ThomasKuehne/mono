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

	internal class X11Clipboard {
		readonly IntPtr DisplayHandle;
		readonly IntPtr CLIPBOARD;
		readonly IntPtr UTF8_STRING;
		readonly IntPtr TARGETS;
		readonly IntPtr FosterParent;

		internal IntPtr ClipMagic;

		internal delegate void UpdateMessageQueueDG (XEventQueue queue, bool allowIdle);

		UpdateMessageQueueDG UpdateMessageQueue;

		internal X11Clipboard (IntPtr display, IntPtr fosterParent, UpdateMessageQueueDG updateMessageQueue)
		{
			DisplayHandle = display;
			FosterParent = fosterParent;
			UpdateMessageQueue = updateMessageQueue;

			source_data = new ListDictionary ();

			CLIPBOARD = XplatUIX11.XInternAtom (display, "CLIPBOARD", false);
			TARGETS = XplatUIX11.XInternAtom (display, "TARGETS", false);
			UTF8_STRING = XplatUIX11.XInternAtom (display, "UTF8_STRING", false);

			Items = new Dictionary<IntPtr, object>();
		}

		ListDictionary source_data;			// Source in its different formats, if any
		string plain_text_source;			// Cached source as plain-text string

		Dictionary<IntPtr, object> Items;	// Object on the clipboard
		ArrayList	Formats;		// list of formats available in the clipboard
		IntPtr		Retrieving;		// non-zero if we are requesting an item
		IntPtr		Enumerating;		// non-zero if we are enumerating through all known types

		void ClearSources ()
		{
			source_data.Clear ();
			plain_text_source = null;
		}

		void AddSource (int type, object source)
		{
			// Try to detect plain text, based on the old behaviour of XplatUIX11, which usually assigns
			// -1 as the type when a string is stored in the Clipboard
			if (source is string && (type == DataFormats.GetFormat (DataFormats.Text).Id || type == -1))
				plain_text_source = source as string;

			source_data [type] = source;
		}

		object GetSource (int type)
		{
			return source_data [type];
		}

		string GetPlainText ()
		{
			return plain_text_source;
		}

		internal bool HandleSelectionRequestEvent (ref XEvent xevent)
		{
			if (xevent.SelectionRequestEvent.selection != CLIPBOARD)
				return false;

					XEvent sel_event;

					sel_event = new XEvent();
					sel_event.SelectionEvent.type = XEventName.SelectionNotify;
					sel_event.SelectionEvent.send_event = true;
					sel_event.SelectionEvent.display = DisplayHandle;
					sel_event.SelectionEvent.selection = xevent.SelectionRequestEvent.selection;
					sel_event.SelectionEvent.target = xevent.SelectionRequestEvent.target;
					sel_event.SelectionEvent.requestor = xevent.SelectionRequestEvent.requestor;
					sel_event.SelectionEvent.time = xevent.SelectionRequestEvent.time;
					sel_event.SelectionEvent.property = IntPtr.Zero;

					IntPtr format_atom = xevent.SelectionRequestEvent.target;

					// Seems that some apps support asking for supported types
					if (format_atom == TARGETS) {
						IntPtr[]	atoms;
						int	atom_count;

						atoms = new IntPtr[5];
						atom_count = 0;

						if (plain_text_source != null) {
							atoms[atom_count++] = (IntPtr)UTF8_STRING;
						}

						XplatUIX11.XChangeProperty(DisplayHandle, xevent.SelectionRequestEvent.requestor, (IntPtr)xevent.SelectionRequestEvent.property,
								(IntPtr)Atom.XA_ATOM, 32, PropertyMode.Replace, atoms, atom_count);
						sel_event.SelectionEvent.property = xevent.SelectionRequestEvent.property;
					} else if (plain_text_source != null && format_atom == UTF8_STRING) {
						IntPtr	buffer = IntPtr.Zero;
						int	buflen;
						Encoding encoding = null;

						buflen = 0;

						// Select an encoding depending on the target
						IntPtr target_atom = xevent.SelectionRequestEvent.target;
						if (target_atom == UTF8_STRING)
							encoding = Encoding.UTF8;

						Byte [] bytes;

						bytes = encoding.GetBytes (GetPlainText ());
						buffer = Marshal.AllocHGlobal (bytes.Length);
						buflen = bytes.Length;

						for (int i = 0; i < buflen; i++)
							Marshal.WriteByte (buffer, i, bytes [i]);

						if (buffer != IntPtr.Zero) {
							XplatUIX11.XChangeProperty(DisplayHandle, xevent.SelectionRequestEvent.requestor, (IntPtr)xevent.SelectionRequestEvent.property, (IntPtr)xevent.SelectionRequestEvent.target, 8, PropertyMode.Replace, buffer, buflen);
							sel_event.SelectionEvent.property = xevent.SelectionRequestEvent.property;
							Marshal.FreeHGlobal(buffer);
						}
					}

					XplatUIX11.XSendEvent(DisplayHandle, xevent.SelectionRequestEvent.requestor, false, new IntPtr ((int)EventMask.NoEventMask), ref sel_event);
			return true;
		}

		internal bool HandleSelectionNotifyEvent (ref XEvent xevent)
		{
					if (Enumerating == xevent.SelectionEvent.selection) {
						if (xevent.SelectionEvent.property != IntPtr.Zero) {
							TranslatePropertyToClipboard (xevent.SelectionEvent.selection,
									xevent.SelectionEvent.property);
						}
						Enumerating = IntPtr.Zero;
						return true;
					} else if (Retrieving == xevent.SelectionEvent.selection) {
						if (xevent.SelectionEvent.property != IntPtr.Zero) {
							TranslatePropertyToClipboard(xevent.SelectionEvent.selection,
									xevent.SelectionEvent.property);
						} else {
							ClearSources ();
							Items.Remove(xevent.SelectionEvent.selection);
						}
						Retrieving = IntPtr.Zero;
						return true;
					}
			return false;
		}

		void TranslatePropertyToClipboard(IntPtr selection, IntPtr property) {
			IntPtr			actual_atom;
			int			actual_format;
			IntPtr			nitems;
			IntPtr			bytes_after;
			IntPtr			prop = IntPtr.Zero;

			Items.Remove(selection);

			XplatUIX11.XGetWindowProperty(DisplayHandle, FosterParent, property, IntPtr.Zero, new IntPtr (0x7fffffff), true, (IntPtr)Atom.AnyPropertyType, out actual_atom, out actual_format, out nitems, out bytes_after, ref prop);

			if ((long)nitems > 0) {
				if (property == UTF8_STRING) {
					byte [] buffer = new byte [(int)nitems];
					for (int i = 0; i < (int)nitems; i++)
						buffer [i] = Marshal.ReadByte (prop, i);
					Items.Add(selection, Encoding.UTF8.GetString (buffer));
				} else if (property == TARGETS) {
					for (int pos = 0; pos < (long) nitems; pos++) {
						IntPtr format = Marshal.ReadIntPtr (prop, pos * IntPtr.Size);
						if (DataFormats.ContainsFormat (format.ToInt32())) {
							Formats.Add (format);
						}
					}
				}

				XplatUIX11.XFree(prop);
			}
		}

		internal int[] ClipboardAvailableFormats(IntPtr handle) {
			int[]			result;


			if (XplatUIX11.XGetSelectionOwner(DisplayHandle, handle) == IntPtr.Zero) {
				return null;
			}

			Formats = new ArrayList();

			// TARGETS is supported by all, no iteration required - see ICCCM chapter 2.6.2. Target Atoms
			Enumerating = handle;
			XplatUIX11.XConvertSelection(DisplayHandle, handle, TARGETS, TARGETS, FosterParent, IntPtr.Zero);

			var timeToWaitForSelectionFormats = TimeSpan.FromSeconds(4);
			var startTime = DateTime.Now;
			while (Enumerating != IntPtr.Zero) {
				UpdateMessageQueue(null, false);

				if (DateTime.Now - startTime > timeToWaitForSelectionFormats)
					break;
			}

			result = new int[Formats.Count];

			for (int i = 0; i < Formats.Count; i++) {
				result[i] = ((IntPtr)Formats[i]).ToInt32 ();
			}

			Formats = null;
			return result;
		}

		internal int ClipboardGetID(string format)
		{
			if (format == "UnicodeText" ) return UTF8_STRING.ToInt32();

			return XplatUIX11.XInternAtom(DisplayHandle, format, false).ToInt32();
		}

		internal object ClipboardRetrieve(IntPtr selection, int type)
		{
			Retrieving = selection;
			XplatUIX11.XConvertSelection(DisplayHandle, selection, (IntPtr)type, (IntPtr)type, FosterParent, IntPtr.Zero);

			while (Retrieving != IntPtr.Zero) {
				UpdateMessageQueue(null, false);
			}

			object obj;
			return Items.TryGetValue(selection, out obj) ? obj : null;
		}

		internal void ClipboardStore (IntPtr handle, object obj, int type, bool copy)
		{
			if (obj != null) {
				AddSource (type, obj);
				XplatUIX11.XSetSelectionOwner (DisplayHandle, handle, FosterParent, IntPtr.Zero);

				if (copy) {
					try {
						var clipboardName = XplatUIX11.XGetAtomName (XplatUIX11.Display, handle);
						var clipboardAtom = XplatUIX11.gdk_atom_intern (clipboardName, true);
						var clipboard = XplatUIX11.gtk_clipboard_get (clipboardAtom);
						if (clipboard != IntPtr.Zero) {
							// for now we only store text
							var text = GetPlainText ();
							if (!string.IsNullOrEmpty (text)) {
								XplatUIX11.gtk_clipboard_set_text (clipboard, text, text.Length);
								XplatUIX11.gtk_clipboard_store (clipboard);
							}
						}
					} catch {
						// ignore any errors - most likely because gtk isn't installed?
					}
				}
			} else {
				// Clearing the selection
				ClearSources ();
				XplatUIX11.XSetSelectionOwner (DisplayHandle, handle, IntPtr.Zero, IntPtr.Zero);
			}
		}
	}
}

