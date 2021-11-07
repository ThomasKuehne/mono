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
// Authors:
//	Thomas Kuehne	thomas@kuehne.cn
//

namespace System.Windows.Forms {

	internal abstract class X11Selection {
		internal readonly IntPtr Selection;

		protected IDataObject Content;

		internal X11Selection (string selection)
		{
			Selection = XplatUIX11.XInternAtom (XplatUIX11.Display, selection, false);
		}

		internal virtual void HandleSelectionRequestEvent (ref XEvent xevent)
		{
Console.Out.WriteLine($"X11Selection.HandleSelectionRequestEvent {xevent}");
			X11SelectionHandler handler = X11SelectionHandler.Find (xevent.SelectionRequestEvent.target);
			if (handler == null) {
				X11SelectionHandler.SetUnsupported (ref xevent);
			} else {
				handler.SetData (ref xevent, Content);
			}
		}

		internal virtual void HandleSelectionNotifyEvent (ref XEvent xevent)
		{
Console.Out.WriteLine($"X11Selection.HandleSelectionNotifyEvent {xevent}");
			// we requested something the source right now doesn't support
			if (xevent.SelectionEvent.property == IntPtr.Zero)
				return;

			X11SelectionHandler handler = X11SelectionHandler.Find ((IntPtr) xevent.SelectionEvent.target);
			if (handler == null)
				return;

			if (Content == null)
				Content = new DataObject ();

			handler.GetData (ref xevent, Content);
		}
		
		internal virtual void HandleSelectionClearEvent (ref XEvent xevent) {
Console.Out.WriteLine($"X11Selection.HandleSelectionClearEvent {xevent}");
			X11SelectionHandler.FreeNativeSelectionBuffers(Selection);
		}
	}
}

