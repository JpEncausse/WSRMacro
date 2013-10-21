using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
using Microsoft.Kinect;
using System.Drawing;

// Inspired by:
// http://channel9.msdn.com/coding4fun/kinect/A-Simple-Gesture-Processing-Framework-for-the-Kinect-For-Windows
namespace net.encausse.sarah {

  // ==========================================
  //  ENUMEATIONS
  // ==========================================

  public enum JointRelationship {
    None,
    Above,
    Below,
    LeftOf,
    RightOf,
    AboveAndRight,
    BelowAndRight,
    AboveAndLeft,
    BelowAndLeft
  }

  // ==========================================
  //  GESTURE
  // ==========================================


  public class Gesture {

    private readonly Guid id;
    private readonly List<GestureComponent> components;
    private readonly String description;
    private int maxExecutionTime = 0;

    // ==========================================
    //  Constructor
    // ==========================================
 
    public Gesture() {
      id = Guid.NewGuid();
      components = new List<GestureComponent>();
    }

    public Gesture(String description, int maxExecutionTime) {
      this.id = Guid.NewGuid();
      this.components = new List<GestureComponent>();

      this.description = description;
      this.maxExecutionTime = maxExecutionTime;
    }

    // ------------------------------------------
    //  Getter / Setter
    // ------------------------------------------

    public Guid Id {
      get { return id; }
    }

    public String Description {
      get { return description; }
    }

    public int MaximumExecutionTime {
      get { return maxExecutionTime; }
      set { this.maxExecutionTime = value; }
    }

    public List<GestureComponent> Components {
      get { return components; }
    }

    public String Url { get; set; } 

    // ------------------------------------------
    //  Parser
    // ------------------------------------------

    public static Gesture Parse(XmlTextReader reader) {
      var description = reader.GetAttribute("description");
      var maxexecutiontime = reader.GetAttribute("maxExecutionTime") ?? "0";
      var url = reader.GetAttribute("url");

      var gesture  = new Gesture(description, Convert.ToInt32(maxexecutiontime));
      gesture.Url = url;
      
      return gesture;
    }
  }

  // ==========================================
  //  GESTURE COMPONENT
  // ==========================================

  public class GestureComponent {

    public GestureComponent() {
      Begin  = JointRelationship.None;
      Ending = JointRelationship.None;
    }

    public GestureComponent(JointType j1, JointType j2, JointRelationship begin, JointRelationship end) {
      Joint1 = j1;
      Joint2 = j2;
      Begin = begin;
      Ending = end;
    }

    // ------------------------------------------
    //  Getter / Setter
    // ------------------------------------------

    public JointType Joint1 { get; set; }
    public JointType Joint2 { get; set; }
    public JointRelationship Begin { get; set; }
    public JointRelationship Ending { get; set; }

    public String Log(String description) {
      return "[" + description + "] " + Begin + " => " + Ending + " (" + Joint1 + " vs " + Joint2 + ")"; 
    }

    // ------------------------------------------
    //  Parser
    // ------------------------------------------

    public static GestureComponent Parse(XmlTextReader reader) {

      GestureComponent component = new GestureComponent();

      var j1  = reader.GetAttribute("firstJoint");
      if (j1 != null) {
        JointType join = (JointType)(Enum.Parse(typeof(JointType), j1));
        component.Joint1 = join;
      }
      
      var j2 = reader.GetAttribute("secondJoint");
      if (j2 != null) {
        JointType join = (JointType)(Enum.Parse(typeof(JointType), j2));
        component.Joint2 = join;
      }

      var begin = reader.GetAttribute("beginningRelationship");
      if (begin != null) {
        JointRelationship rs = (JointRelationship)(Enum.Parse(typeof(JointRelationship), begin));
        component.Begin = rs;
      }

      var end = reader.GetAttribute("endingRelationship");
      if (end != null) {
        JointRelationship rs = (JointRelationship)(Enum.Parse(typeof(JointRelationship), end));
        component.Ending = rs;
      }

      return component;
    }
  }

  // ==========================================
  //  GESTURE STATE
  // ==========================================

  public class GestureState {
    private DateTime beginExecutionTime;
    private readonly Gesture gesture;

    public GestureState(Gesture gesture) {
      this.gesture = gesture;
      ComponentStates = new List<GestureComponentState>();

      foreach (var component in gesture.Components) {
        var state = new GestureComponentState(component);
        ComponentStates.Add(state);
      }

      IsExecuting = false;
    }

    public void Reset() {
      foreach (var component in ComponentStates) {
        component.Reset();
      }
      IsExecuting = false;
      beginExecutionTime = DateTime.MinValue;
    }

    public Gesture Gesture  { get { return gesture; }}
    public bool IsExecuting { get; private set; }
    public List<GestureComponentState> ComponentStates { get;  set; }

    public bool Evaluate(Skeleton sd, DateTime currentTime) {

      // Check recognition timeout
      if (IsExecuting) {
        TimeSpan executiontime = currentTime - beginExecutionTime;
        if (executiontime.TotalMilliseconds > gesture.MaximumExecutionTime && gesture.MaximumExecutionTime > 0) {
          Reset();
          return false;
        }
      }

      // Check each component of the gesture
      foreach (var component in ComponentStates) {
        if (component.Evaluate(sd)) {
          WSRConfig.GetInstance().logDebug("GESTURE", "Gesture Component: " + gesture.Description);
        }
      }

      // Check gesture is completed
      var inflightCount = 0;
      var completeCount = 0;

      foreach (var component in ComponentStates) {
        if (component.IsBegin)  inflightCount++;
        if (component.IsEnding) completeCount++;
      }

      if (completeCount >= ComponentStates.Count && IsExecuting) {
        WSRConfig.GetInstance().logDebug("GESTURE", "Gesture complete: " + gesture.Description);
        WSRConfig.GetInstance().logDebug("GESTURE", ">>>> RESET <<<<");
        Reset();
        return true;
      }

      // Some components match
      if (inflightCount >= ComponentStates.Count) {
        if (!IsExecuting) {
          WSRConfig.GetInstance().logDebug("GESTURE", "Has Transitioned To In Flight State: " + gesture.Description);
          IsExecuting = true;
          beginExecutionTime = DateTime.Now;
          return false;
        }
      }

      return false;
    }
  }

  // ==========================================
  //  GESTURE COMPONENT STATE
  // ==========================================

  public class GestureComponentState {
    private readonly GestureComponent component;
    private bool isBegin;
    private bool isEnding;

    public GestureComponentState(GestureComponent component) {
      this.component = component;
      Reset();
    }

    public void Reset() {
      isBegin = false;
      isEnding = false;
    }

    public bool Evaluate(Skeleton skeleton) {
      var sjoint1 = skeleton.Joints[component.Joint1]; //.ScaleTo(xScale, yScale);
      var sjoint2 = skeleton.Joints[component.Joint2]; //.ScaleTo(xScale, yScale);

      if (sjoint1.TrackingState == JointTrackingState.Inferred ||
          sjoint1.TrackingState == JointTrackingState.Inferred) {
        return false;
      }

      if (!isBegin) {
        isBegin = CompareJointRelationship(sjoint1, sjoint2, component.Begin);
      }

      if (isBegin && !isEnding) {
        return isEnding = CompareJointRelationship(sjoint1, sjoint2, component.Ending);
      }

      return true;
    }

    private bool CompareJointRelationship(Joint inJoint1, Joint inJoint2, JointRelationship relation) {
      switch (relation) {
        case JointRelationship.None:
          return true;
        case JointRelationship.AboveAndLeft:
          return ((inJoint1.Position.X < inJoint2.Position.X) && (inJoint2.Position.Y < inJoint1.Position.Y));
        case JointRelationship.AboveAndRight:
          return ((inJoint1.Position.X > inJoint2.Position.X) && (inJoint2.Position.Y < inJoint1.Position.Y));
        case JointRelationship.BelowAndLeft:
          return ((inJoint1.Position.X < inJoint2.Position.X) && (inJoint2.Position.Y > inJoint1.Position.Y));
        case JointRelationship.BelowAndRight:
          return ((inJoint1.Position.X > inJoint2.Position.X) && (inJoint2.Position.Y > inJoint1.Position.Y));
        case JointRelationship.Below:
          return inJoint2.Position.Y > inJoint1.Position.Y;
        case JointRelationship.Above:
          return inJoint2.Position.Y < inJoint1.Position.Y;
        case JointRelationship.LeftOf:
          return inJoint1.Position.X < inJoint2.Position.X;
        case JointRelationship.RightOf:
          return inJoint1.Position.X > inJoint2.Position.X;
      }
      return false;
    }

    // ------------------------------------------
    //  Getter / Setter
    // ------------------------------------------

    public bool IsBegin {
      get { if (component.Begin == JointRelationship.None) { isBegin = true; }  return isBegin; }
    }

    public bool IsEnding {
      get { if (component.Ending == JointRelationship.None) { isEnding = true; } return isEnding; }
    }

    public GestureComponent Component {
      get { return component; }
    }
  }

  // ==========================================
  //  GESTURE STATE USER
  // ==========================================
  // A Map of GestureState for each active user

  public class GestureStateUser {
    private readonly List<GestureState> gestureState;
    public DateTime LastGestureCompletionTime;

    public GestureStateUser(List<Gesture> gestures) {
      gestureState = new List<GestureState>();
      foreach (var gesture in gestures) {
        var state = new GestureState(gesture);
        gestureState.Add(state);
      }
    }

    public void ResetAll() {
      foreach (var state in gestureState) {
        state.Reset();
      }
    }

    public Gesture Evaluate(Skeleton skeleton) {
      Gesture match = null;
      foreach (var state in gestureState) {

        // Check gesture state complete
        if (!state.Evaluate(skeleton, DateTime.Now)) {
          continue;
        }

        // Store the most complicated gesture
        if (match == null || match.Components.Count <= state.Gesture.Components.Count) {
          LastGestureCompletionTime = DateTime.Now;
          match = state.Gesture;
        }
        
      }
      return match;
    }
  }

  // ==========================================
  //  GESTURE MANAGER
  // ==========================================

  public class GestureManager {

    public int gestureResetTimeout = 500;
    public List<Gesture> gestures = new List<Gesture>();
    private Dictionary<String, GestureStateUser> userMap = new Dictionary<String, GestureStateUser>();
    
    private WSRKinectSensor Sensor;
    public GestureManager(WSRKinectSensor sensor) {
      this.Sensor = sensor;
    }

    // ------------------------------------------
    //  Load gesture
    // ------------------------------------------

    public void Load() {
      foreach (string directory in WSRConfig.GetInstance().directories) {
        DirectoryInfo d = new DirectoryInfo(directory);
        LoadGestures(d);
      }
    }

    public void LoadGesture(String file, String name) {
      WSRConfig.GetInstance().logInfo("GESTURE", "Load file: " + name + " : " + file);
      Load(file);
    }

    public void LoadGestures(DirectoryInfo dir) {
      WSRConfig.GetInstance().logDebug("GESTURE", "Load directory: " + dir.FullName);

      // Load Grammar
      foreach (FileInfo f in dir.GetFiles("*.gesture")) {
        LoadGesture(f.FullName, f.Name);
      }

      // Recursive directory
      foreach (DirectoryInfo d in dir.GetDirectories()) {
        LoadGestures(d);
      }
    }

    public void Load(String xmlFile) {
      var reader = new XmlTextReader(xmlFile);
      Gesture gesture = null;
      while (reader.Read()) {
        switch (reader.NodeType) {
          case XmlNodeType.Element:
            if (reader.Name == "gestures") {
              var timeout = reader.GetAttribute("gestureResetTimeout");
              gestureResetTimeout = timeout != null ? Convert.ToInt32(timeout) : gestureResetTimeout;
            }
            if (reader.Name == "gesture") {
              gesture = Gesture.Parse(reader);
              gestures.Add(gesture);
              WSRConfig.GetInstance().logInfo("GESTURE", "Loading: " + gesture.Description);
            }
            if (reader.Name == "component" && gesture != null) {
              GestureComponent component = GestureComponent.Parse(reader);
              gesture.Components.Add(component);
              WSRConfig.GetInstance().logDebug("GESTURE", "Component: " + component.Log(gesture.Description));
            }
            break;
        }
      }
    }

    // ------------------------------------------
    //  Skeleton
    // ------------------------------------------

    protected Gesture match; // the current matching gesture
    public Skeleton Skeleton = null;

    public Gesture CheckGestures(Skeleton[] skeletons) {

      // Clean Prefetch
      Skeleton = null;

      if (null == skeletons) { return null; }
      foreach (Skeleton sd in skeletons) {

        // Too large array
        if (sd == null) { continue; }

        // If this skeleton is no longer being tracked, skip it
        if (sd.TrackingState != SkeletonTrackingState.Tracked) {
          continue;
        }

        // Prefetch some 
        Prefetch(sd);

        // Validate all joints some data
        if (!Viewport(sd)) { continue; }
        Skeleton = sd;

        // If there is not already a gesture state map for this skeleton, then create one
        if (!userMap.ContainsKey("P_"+sd.TrackingId)) {
          var user = new GestureStateUser(gestures);
          userMap.Add("P_"+sd.TrackingId, user);
        }


        Gesture gesture = userMap["P_" + sd.TrackingId].Evaluate(sd);
        if (null != gesture) {

          // Store the Gesture matching the most components
          if (match == null || match.Components.Count <= gesture.Components.Count) {
            match = gesture;
          }

          // Do not fire immediatly, wait a little bit
          Gesture g = MatchGesture();
          if (g != null) { return g; }
        }

        break; // The skeleton match
      }

      // At last return gesture or null
      return MatchGesture();
    }

    protected DateTime Threshold = DateTime.Now;
    protected Gesture MatchGesture() {
      
      // Wait for a given amount of time
      if ((DateTime.Now - Threshold).TotalMilliseconds < 1000) { return null; }

      // Reset threashold
      Threshold = DateTime.Now;

      if (Skeleton != null && userMap.ContainsKey("P_" + Skeleton.TrackingId)) {
        userMap["P_"+Skeleton.TrackingId].ResetAll();
      }

      // No match
      Gesture g = match;
      if (null == g){ return null; }

      // Clean and match
      match = null;
      return g;
    }

    // ------------------------------------------
    //  Pre Fetch
    // ------------------------------------------

    protected bool Viewport(Skeleton sk) {
      
      bool track = sk.Joints[JointType.Head].TrackingState == JointTrackingState.Tracked
                && sk.Joints[JointType.WristLeft].TrackingState == JointTrackingState.Tracked
                && sk.Joints[JointType.WristRight].TrackingState == JointTrackingState.Tracked
                && sk.Joints[JointType.HipLeft].TrackingState == JointTrackingState.Tracked
                && sk.Joints[JointType.HipRight].TrackingState == JointTrackingState.Tracked;

      if (track) {
        var head   = sk.Joints[JointType.Head].Position;
        var kludge = sk.Joints[JointType.FootLeft].Position;
        var diff = Math.Abs(head.Y - kludge.Y) * 100;
        track = diff > WSRConfig.GetInstance().GestureFix;
        // WSRConfig.GetInstance().logInfo("GESTURE", " Kludge: " + diff + "cm");
      }

      return track;
    }

    
    protected void Prefetch(Skeleton sk) {

      // Store head in milimeters like depth
      var head = sk.Joints[JointType.Head];
      if (head.TrackingState == JointTrackingState.Tracked) {
        var pos = head.Position;
        WSRProfileManager.GetInstance().UpdateHead(pos.X * 1000f, pos.Y * 1000f, pos.Z * 1000f);
      }

      // Store height in m
      Joint p1 = sk.Joints[JointType.ElbowLeft];
      Joint p2 = sk.Joints[JointType.WristLeft];
      if (p1.TrackingState == JointTrackingState.Tracked && p2.TrackingState == JointTrackingState.Tracked) {
        var height = Math.Round((SkeletalExtensions.Length(p1, p2) * 100 * 5.27 + 6 + 42.05) / 100, 2);
        WSRProfileManager.GetInstance().UpdateHeight(height);
      }
    }
  }
}
