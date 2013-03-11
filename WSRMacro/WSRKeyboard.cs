using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowsInput;

namespace net.encausse.sarah {

  public class WSRKeyboard {

    // Singleton
    private static WSRKeyboard manager;
    public static WSRKeyboard GetInstance() {
      if (manager == null) {
        manager = new WSRKeyboard();
      }
      return manager;
    }

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    public WSRKeyboard() {
    
    }

    // ==========================================
    //  PRIVATE
    // ==========================================

    protected VirtualKeyCode KeyCode (String key) {
      int code = int.Parse(key);
      return (VirtualKeyCode) code;
    }

    // ==========================================
    //  UTILITY
    // ==========================================

    public void SimulateTextEntry(String text) {
      InputSimulator.SimulateTextEntry(text);
    }

    public void SimulateKey(String key, int type, String mod) {
      WSRConfig.GetInstance().logInfo("KEYBOARD", "SimulateKey " + key + " + " + mod);
      if (mod != null && mod != "") {
        InputSimulator.SimulateModifiedKeyStroke(KeyCode(mod), KeyCode(key));
        return;
      }
           if (type == 0) { InputSimulator.SimulateKeyPress(KeyCode(key)); }
      else if (type == 1) { InputSimulator.SimulateKeyDown(KeyCode(key));  }
      else if (type == 2) { InputSimulator.SimulateKeyUp(KeyCode(key));    }
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow (IntPtr hWnd);
    public void ActivateApp (string processName) {
      // Activate the first application we find with this name
      Process[] p = Process.GetProcessesByName(processName);
      if (p.Length > 0) {
        SetForegroundWindow(p[0].MainWindowHandle);
        WSRConfig.GetInstance().logInfo("KEYBOARD", "Activate " + p[0].ProcessName);
      }
    }

    public void RunApp(String processName, String param) {
      try {
        Process.Start(processName, param);
      } catch (Exception ex){
        WSRConfig.GetInstance().logError("KEYBOARD", ex);
      }
    }
  }
}
