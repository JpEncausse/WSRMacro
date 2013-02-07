using System;
using System.Windows.Forms;
using System.Diagnostics;
using net.encausse.sarah.Properties;

namespace net.encausse.sarah {
  class WSRTrayMenu : IDisposable {

    // The NotifyIcon object.
    NotifyIcon ni;

    // Initializes a new instance of the <see cref="ProcessIcon"/> class.
    public WSRTrayMenu() {
      // Instantiate the NotifyIcon object.
      ni = new NotifyIcon();
    }

    // Displays the icon in the system tray.
    public void Display() {

      // Put the icon in the system tray and allow it react to mouse clicks.			
      ni.MouseClick += new MouseEventHandler(ni_MouseClick);
      ni.Icon = Resources.Home;
      ni.Text = "SARAH Speech Recognition";
      ni.Visible = true;

      // Attach a context menu.
      ni.ContextMenuStrip = new WSRCtxMenu().Create();
    }

    // Releases unmanaged and - optionally - managed resources
    public void Dispose() {
      // When the application closes, this will remove the icon from the system tray immediately.
      ni.Dispose();
    }

    // Handles the MouseClick event of the ni control.
    void ni_MouseClick(object sender, MouseEventArgs e) {
      // Handle mouse button clicks.
      if (e.Button == MouseButtons.Left) {
        // Start Windows Explorer.
        Process.Start("explorer", WSRConfig.GetInstance().getDirectory());
      }
    }
  }
}