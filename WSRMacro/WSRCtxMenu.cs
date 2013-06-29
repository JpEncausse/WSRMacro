using System;
using System.Windows.Forms;
using System.Drawing;
using net.encausse.sarah.Properties;

namespace net.encausse.sarah {

  class WSRCtxMenu {

    // Constructor
    public WSRCtxMenu() { }
    
    // Create menu
    public ContextMenuStrip Create() {

      // Add the default menu options.
      ContextMenuStrip menu = new ContextMenuStrip();
      ToolStripMenuItem item;

      // WSRMacro Hook
      WSRConfig.GetInstance().GetWSRMicro().HandleCtxMenu(menu);

      // Separator.
      ToolStripSeparator sep;
      sep = new ToolStripSeparator();
      menu.Items.Add(sep);

      // Exit.
      item = new ToolStripMenuItem();
      item.Text = "Exit";
      item.Click += new System.EventHandler(Exit_Click);
      item.Image = Resources.Exit; 
      menu.Items.Add(item);

      return menu;
    }

    // ==========================================
    //  CALLBACK
    // ==========================================

    void Exit_Click(object sender, EventArgs e) {
      WSRConfig.GetInstance().GetWSRMicro().Dispose();
      Application.Exit();
    }
  }
}
