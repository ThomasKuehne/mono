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
		internal readonly IntPtr DELETE;

		protected IDataObject Content;
		protected int ConvertsPending;

		internal X11Selection (string selection)
		{
			Selection = XplatUIX11.XInternAtom (XplatUIX11.Display, selection, false);
			DELETE = XplatUIX11.XInternAtom (XplatUIX11.Display, "DELETE", false);
		}

		internal virtual void HandleSelectionRequestEvent (ref XEvent xevent)
		{

			if (xevent.SelectionRequestEvent.target == DELETE) {
Console.Out.WriteLine("X11Selection.HandleSelectionRequestEvent DELETE");
				// we are only clearing the buffer and not actualy "deleting" the content
				// there doesn't seem to be any way to ask an dotnet application to "delete"
				XplatUIX11.XSetSelectionOwner (XplatUIX11.Display, Selection, IntPtr.Zero, IntPtr.Zero);
				X11SelectionHandler.SetEmpty (ref xevent);
			} else {
				X11SelectionHandler handler = X11SelectionHandler.Find (xevent.SelectionRequestEvent.target);
				if (handler == null) {
Console.Out.WriteLine("X11Selection.HandleSelectionRequestEvent Unsupported <= {0}",
	XplatUIX11.XGetAtomName (XplatUIX11.Display, xevent.SelectionRequestEvent.target));
					X11SelectionHandler.SetUnsupported (ref xevent);
				} else {
Console.Out.WriteLine("X11Selection.HandleSelectionRequestEvent OK <= {0}",
	XplatUIX11.XGetAtomName (XplatUIX11.Display, xevent.SelectionRequestEvent.target));
					handler.SetData (ref xevent, Content);
				}
			}
		}

		internal virtual void HandleSelectionNotifyEvent (ref XEvent xevent)
		{
Console.Out.WriteLine("X11Selection.HandleSelectionNotifyEvent {0} {1}",
	XplatUIX11.XGetAtomName (XplatUIX11.Display, xevent.SelectionEvent.target),
	xevent.SelectionEvent.property);

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
Console.Out.WriteLine("X11Selection.HandleSelectionClearEvent");
			X11SelectionHandler.FreeNativeSelectionBuffers(Selection);
		}
	}
}

