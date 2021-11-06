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
// Copyright (c) 2009 Novell, Inc.
//
// Authors:
//	Carlos Alberto Cortez (calberto.cortez@gmail.com)
//

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System;

namespace System.Windows.Forms {

	internal class X11Clipboard {
		readonly IntPtr DisplayHandle;
		readonly IntPtr CLIPBOARD;
		readonly IntPtr OEMTEXT;
		readonly IntPtr RICHTEXTFORMAT;
		readonly IntPtr UTF8_STRING;
		readonly IntPtr UTF16_STRING;
		readonly IntPtr TARGETS;

		internal X11Clipboard (IntPtr display)
		{
			DisplayHandle = display;
			CLIPBOARD = XplatUIX11.XInternAtom (display, "CLIPBOARD", false);
			OEMTEXT = XplatUIX11.XInternAtom (display, "OEMTEXT", false);
			RICHTEXTFORMAT = XplatUIX11.XInternAtom (display, "RICHTEXTFORMAT", false);
			TARGETS = XplatUIX11.XInternAtom (display, "TARGETS", false);
			UTF16_STRING = XplatUIX11.XInternAtom (display, "UTF16_STRING", false);
			UTF8_STRING = XplatUIX11.XInternAtom (display, "UTF8_STRING", false);
			source_data = new ListDictionary ();
		}

		ListDictionary source_data;			// Source in its different formats, if any
		string plain_text_source;			// Cached source as plain-text string
		Image image_source;				// Cached source as image

		internal object		Item;			// Object on the clipboard
		internal ArrayList	Formats;		// list of formats available in the clipboard
		internal bool		Retrieving;		// true if we are requesting an item
		internal bool		Enumerating;		// true if we are enumerating through all known types
		internal XplatUI.ObjectToClipboard Converter;

		public void ClearSources ()
		{
			source_data.Clear ();
			plain_text_source = null;
			image_source = null;
		}

		public void AddSource (int type, object source)
		{
			// Try to detect plain text, based on the old behaviour of XplatUIX11, which usually assigns
			// -1 as the type when a string is stored in the Clipboard
			if (source is string && (type == DataFormats.GetFormat (DataFormats.Text).Id || type == -1))
				plain_text_source = source as string;
			else if (source is Image)
				image_source = source as Image;

			source_data [type] = source;
		}

		public object GetSource (int type)
		{
			return source_data [type];
		}

		public string GetPlainText ()
		{
			return plain_text_source;
		}

		public string GetRtfText ()
		{
			DataFormats.Format format = DataFormats.GetFormat (DataFormats.Rtf);
			if (format == null)
				return null; // FIXME - is RTF not supported on any system?

			return (string)GetSource (format.Id);
		}

		public Image GetImage ()
		{
			return image_source;
		}

		public bool IsSourceText {
			get {
				return plain_text_source != null;
			}
		}

		public bool IsSourceImage {
			get {
				return image_source != null;
			}
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

						if (IsSourceText) {
							atoms[atom_count++] = (IntPtr)Atom.XA_STRING;
							atoms[atom_count++] = (IntPtr)OEMTEXT;
							atoms[atom_count++] = (IntPtr)UTF8_STRING;
							atoms[atom_count++] = (IntPtr)UTF16_STRING;
							atoms[atom_count++] = (IntPtr)RICHTEXTFORMAT;
						} else if (IsSourceImage) {
							atoms[atom_count++] = (IntPtr)Atom.XA_PIXMAP;
							atoms[atom_count++] = (IntPtr)Atom.XA_BITMAP;
						} else {
							// FIXME - handle other types
						}

						XplatUIX11.XChangeProperty(DisplayHandle, xevent.SelectionRequestEvent.requestor, (IntPtr)xevent.SelectionRequestEvent.property,
								(IntPtr)Atom.XA_ATOM, 32, PropertyMode.Replace, atoms, atom_count);
						sel_event.SelectionEvent.property = xevent.SelectionRequestEvent.property;
					} else if (format_atom == (IntPtr)RICHTEXTFORMAT) {
						string rtf_text = GetRtfText ();
						if (rtf_text != null) {
							// The RTF spec mentions that ascii is enough to contain it
							Byte [] bytes = Encoding.ASCII.GetBytes (rtf_text);
							int buflen = bytes.Length;
							IntPtr buffer = Marshal.AllocHGlobal (buflen);

							for (int i = 0; i < buflen; i++)
								Marshal.WriteByte (buffer, i, bytes[i]);

							XplatUIX11.XChangeProperty(DisplayHandle, xevent.SelectionRequestEvent.requestor, (IntPtr)xevent.SelectionRequestEvent.property,
									(IntPtr)xevent.SelectionRequestEvent.target, 8, PropertyMode.Replace, buffer, buflen);
							sel_event.SelectionEvent.property = xevent.SelectionRequestEvent.property;
							Marshal.FreeHGlobal(buffer);
						}
					} else if (IsSourceText &&
					           (format_atom == (IntPtr)Atom.XA_STRING
					            || format_atom == OEMTEXT
					            || format_atom == UTF16_STRING
					            || format_atom == UTF8_STRING)) {
						IntPtr	buffer = IntPtr.Zero;
						int	buflen;
						Encoding encoding = null;

						buflen = 0;

						// Select an encoding depending on the target
						IntPtr target_atom = xevent.SelectionRequestEvent.target;
						if (target_atom == (IntPtr)Atom.XA_STRING || target_atom == OEMTEXT)
							// FIXME - EOMTEXT should encode into ISO2022
							encoding = Encoding.ASCII;
						else if (target_atom == UTF16_STRING)
							encoding = Encoding.Unicode;
						else if (target_atom == UTF8_STRING)
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
					} else if (GetSource (format_atom.ToInt32 ()) != null) { // check if we have an available value of this format
						if (DataFormats.GetFormat (format_atom.ToInt32 ()).is_serializable) {
							object serializable = GetSource (format_atom.ToInt32 ());

							BinaryFormatter formatter = new BinaryFormatter ();
							MemoryStream memory_stream = new MemoryStream ();
							formatter.Serialize (memory_stream, serializable);

							int buflen = (int)memory_stream.Length;
							IntPtr buffer = Marshal.AllocHGlobal (buflen);
							memory_stream.Position = 0;
							for (int i = 0; i < buflen; i++)
								Marshal.WriteByte (buffer, i, (byte)memory_stream.ReadByte ());
							memory_stream.Close ();

							XplatUIX11.XChangeProperty (DisplayHandle, xevent.SelectionRequestEvent.requestor, (IntPtr)xevent.SelectionRequestEvent.property, (IntPtr)xevent.SelectionRequestEvent.target,
									8, PropertyMode.Replace, buffer, buflen);
							sel_event.SelectionEvent.property = xevent.SelectionRequestEvent.property;
							Marshal.FreeHGlobal (buffer);
						}

					} else if (IsSourceImage) {
						if (xevent.SelectionEvent.target == (IntPtr)Atom.XA_PIXMAP) {
							// FIXME - convert image and store as property
						} else if (xevent.SelectionEvent.target == (IntPtr)Atom.XA_PIXMAP) {
							// FIXME - convert image and store as property
						}
					}

					XplatUIX11.XSendEvent(DisplayHandle, xevent.SelectionRequestEvent.requestor, false, new IntPtr ((int)EventMask.NoEventMask), ref sel_event);
			return true;
		}
	}
}

