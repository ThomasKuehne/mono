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
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System;

namespace System.Windows.Forms {

	internal class X11Clipboard {
		readonly IntPtr DisplayHandle;
		readonly IntPtr CLIPBOARD;
		readonly IntPtr UTF8_STRING;
		readonly IntPtr UTF16_STRING;
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
			UTF16_STRING = XplatUIX11.XInternAtom (display, "UTF16_STRING", false);
			UTF8_STRING = XplatUIX11.XInternAtom (display, "UTF8_STRING", false);
		}

		ListDictionary source_data;			// Source in its different formats, if any
		string plain_text_source;			// Cached source as plain-text string

		object		Item;			// Object on the clipboard
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

		bool IsSourceText {
			get {
				return plain_text_source != null;
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
							atoms[atom_count++] = (IntPtr)UTF8_STRING;
							atoms[atom_count++] = (IntPtr)UTF16_STRING;
						} else {
							// FIXME - handle other types
						}

						XplatUIX11.XChangeProperty(DisplayHandle, xevent.SelectionRequestEvent.requestor, (IntPtr)xevent.SelectionRequestEvent.property,
								(IntPtr)Atom.XA_ATOM, 32, PropertyMode.Replace, atoms, atom_count);
						sel_event.SelectionEvent.property = xevent.SelectionRequestEvent.property;
					} else if (IsSourceText &&
					           (format_atom == (IntPtr)Atom.XA_STRING
					            || format_atom == UTF16_STRING
					            || format_atom == UTF8_STRING)) {
						IntPtr	buffer = IntPtr.Zero;
						int	buflen;
						Encoding encoding = null;

						buflen = 0;

						// Select an encoding depending on the target
						IntPtr target_atom = xevent.SelectionRequestEvent.target;
						if (target_atom == (IntPtr)Atom.XA_STRING)
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

					}

					XplatUIX11.XSendEvent(DisplayHandle, xevent.SelectionRequestEvent.requestor, false, new IntPtr ((int)EventMask.NoEventMask), ref sel_event);
			return true;
		}

		internal bool HandleSelectionNotifyEvent (ref XEvent xevent)
		{
					if (Enumerating == xevent.SelectionEvent.selection) {
						if (xevent.SelectionEvent.property != IntPtr.Zero) {
							TranslatePropertyToClipboard (xevent.SelectionEvent.property);
						}
						Enumerating = IntPtr.Zero;
						return true;
					} else if (Retrieving == xevent.SelectionEvent.selection) {
						if (xevent.SelectionEvent.property != IntPtr.Zero) {
							TranslatePropertyToClipboard(xevent.SelectionEvent.property);
						} else {
							ClearSources ();
							Item = null;
						}
						Retrieving = IntPtr.Zero;
						return true;
					}
			return false;
		}

		void TranslatePropertyToClipboard(IntPtr property) {
			IntPtr			actual_atom;
			int			actual_format;
			IntPtr			nitems;
			IntPtr			bytes_after;
			IntPtr			prop = IntPtr.Zero;

			Item = null;

			XplatUIX11.XGetWindowProperty(DisplayHandle, FosterParent, property, IntPtr.Zero, new IntPtr (0x7fffffff), true, (IntPtr)Atom.AnyPropertyType, out actual_atom, out actual_format, out nitems, out bytes_after, ref prop);

			if ((long)nitems > 0) {
				if (property == (IntPtr)Atom.XA_STRING) {
					// Xamarin-5116: PtrToStringAnsi expects to get UTF-8, but we might have
					// Latin-1 instead, in which case it will return null.
					var s = Marshal.PtrToStringAnsi (prop);
					if (string.IsNullOrEmpty (s)) {
						var sb = new StringBuilder ();
						for (int i = 0; i < (int)nitems; i++) {
							var b = Marshal.ReadByte (prop, i);
							sb.Append ((char)b);
						}
						s = sb.ToString ();
					}
					// Some X managers/apps pass unicode chars as escaped strings, so
					// we may need to unescape them.
					Item = UnescapeUnicodeFromAnsi (s);
				} else if (property == UTF8_STRING) {
					byte [] buffer = new byte [(int)nitems];
					for (int i = 0; i < (int)nitems; i++)
						buffer [i] = Marshal.ReadByte (prop, i);
					Item = Encoding.UTF8.GetString (buffer);
				} else if (property == UTF16_STRING) {
					byte [] buffer = new byte [(int)nitems];
					for (int i = 0; i < (int)nitems; i++)
						buffer [i] = Marshal.ReadByte (prop, i);
					Item = Encoding.Unicode.GetString (buffer);
				} else if (property == TARGETS) {
					for (int pos = 0; pos < (long) nitems; pos++) {
						IntPtr format = Marshal.ReadIntPtr (prop, pos * IntPtr.Size);
						if (DataFormats.ContainsFormat (format.ToInt32())) {
							Formats.Add (format);
						}
					}
				} else if (DataFormats.ContainsFormat (property.ToInt32 ())) {
					if (DataFormats.GetFormat (property.ToInt32 ()).is_serializable) {
						MemoryStream memory_stream = new MemoryStream ((int)nitems);
						for (int i = 0; i < (int)nitems; i++)
							memory_stream.WriteByte (Marshal.ReadByte (prop, i));

						memory_stream.Position = 0;
						BinaryFormatter formatter = new BinaryFormatter ();
						Item = formatter.Deserialize (memory_stream);
						memory_stream.Close ();
					}
				}

				XplatUIX11.XFree(prop);
			}
		}

		static string UnescapeUnicodeFromAnsi (string value)
		{
			if (value == null || value.IndexOf ("\\u") == -1)
				return value;

			StringBuilder sb = new StringBuilder (value.Length);
			int start, pos;

			start = pos = 0;
			while (start < value.Length) {
				pos = value.IndexOf ("\\u", start);
				if (pos == -1)
					break;

				sb.Append (value, start, pos - start);
				pos += 2;
				start = pos;

				int length = 0;
				while (pos < value.Length && length < 4) {
					if (!ValidHexDigit (value [pos]))
						break;
					length++;
					pos++;
				}

				int res;
				if (!Int32.TryParse (value.Substring (start, length), System.Globalization.NumberStyles.HexNumber,
							null, out res))
					return value; // Error, return the unescaped original value.

				sb.Append ((char)res);
				start = pos;
			}

			// Append any remaining data.
			if (start < value.Length)
				sb.Append (value, start, value.Length - start);

			return sb.ToString ();
		}

		static bool ValidHexDigit (char e)
		{
			return Char.IsDigit (e) || (e >= 'A' && e <= 'F') || (e >= 'a' && e <= 'f');
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
			if (format == "Text" ) return (int)Atom.XA_STRING;
			else if (format == "Bitmap" ) return (int)Atom.XA_BITMAP;
			//else if (format == "MetaFilePict" ) return 3;
			//else if (format == "SymbolicLink" ) return 4;
			//else if (format == "DataInterchangeFormat" ) return 5;
			//else if (format == "Tiff" ) return 6;
			else if (format == "DeviceIndependentBitmap" ) return (int)Atom.XA_PIXMAP;
			else if (format == "Palette" ) return (int)Atom.XA_COLORMAP;	// Useless
			//else if (format == "PenData" ) return 10;
			//else if (format == "RiffAudio" ) return 11;
			//else if (format == "WaveAudio" ) return 12;
			else if (format == "UnicodeText" ) return UTF8_STRING.ToInt32();
			//else if (format == "EnhancedMetafile" ) return 14;
			//else if (format == "FileDrop" ) return 15;
			//else if (format == "Locale" ) return 16;

			return XplatUIX11.XInternAtom(DisplayHandle, format, false).ToInt32();
		}

		internal object ClipboardRetrieve(IntPtr handle, int type)
		{
			Retrieving = handle;
			XplatUIX11.XConvertSelection(DisplayHandle, handle, (IntPtr)type, (IntPtr)type, FosterParent, IntPtr.Zero);

			while (Retrieving != IntPtr.Zero) {
				UpdateMessageQueue(null, false);
			}

			return Item;
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

