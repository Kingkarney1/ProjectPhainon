using System;
using UnityEngine.InputSystem;

namespace Phainon
{
    /// <summary>
    /// All player input in one place, built on the Input System package. Bindings are declared here
    /// rather than scattered through gameplay code, so rebinding or adding a gamepad later is one edit.
    /// </summary>
    public class PhainonInput : IDisposable
    {
        public readonly InputAction Point;
        public readonly InputAction LookDelta;
        public readonly InputAction Interact;
        public readonly InputAction LookHold;
        public readonly InputAction Scroll;
        public readonly InputAction Zoom;
        public readonly InputAction Pause;
        public readonly InputAction ToggleGuide;
        public readonly InputAction ThrottleAxis;
        public readonly InputAction ThrottleFull;
        public readonly InputAction ThrottleCut;
        public readonly InputAction Pitch;
        public readonly InputAction Yaw;
        public readonly InputAction Roll;
        public readonly InputAction[] ViewPresets;
        public readonly InputAction[] Replies;
        public readonly InputAction ToggleCommsLog;
        public readonly InputAction TimeFaster;
        public readonly InputAction TimeSlower;

        private readonly InputActionMap map = new InputActionMap("Cockpit");

        public PhainonInput()
        {
            Point = map.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position");
            LookDelta = map.AddAction("LookDelta", InputActionType.PassThrough, "<Pointer>/delta");
            Interact = map.AddAction("Interact", InputActionType.Button, "<Mouse>/leftButton");
            LookHold = map.AddAction("LookHold", InputActionType.Button, "<Mouse>/rightButton");
            Scroll = map.AddAction("Scroll", InputActionType.PassThrough, "<Mouse>/scroll/y");
            Zoom = map.AddAction("Zoom", InputActionType.Button, "<Mouse>/middleButton");
            Zoom.AddBinding("<Keyboard>/z");
            Pause = map.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            Pause.AddBinding("<Keyboard>/p");
            ToggleGuide = map.AddAction("ToggleGuide", InputActionType.Button, "<Keyboard>/h");
            ThrottleFull = map.AddAction("ThrottleFull", InputActionType.Button, "<Keyboard>/t");
            ThrottleCut = map.AddAction("ThrottleCut", InputActionType.Button, "<Keyboard>/x");

            ThrottleAxis = map.AddAction("ThrottleAxis", InputActionType.Value);
            ThrottleAxis.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftCtrl").With("Positive", "<Keyboard>/leftShift");

            Pitch = map.AddAction("Pitch", InputActionType.Value);
            Pitch.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/w").With("Positive", "<Keyboard>/s");
            Yaw = map.AddAction("Yaw", InputActionType.Value);
            Yaw.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            Roll = map.AddAction("Roll", InputActionType.Value);
            Roll.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");

            ViewPresets = new InputAction[6];
            for (int i = 0; i < ViewPresets.Length; i++)
                ViewPresets[i] = map.AddAction("View" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));

            // Radio replies sit on the number keys after the view presets.
            Replies = new InputAction[3];
            for (int i = 0; i < Replies.Length; i++)
                Replies[i] = map.AddAction("Reply" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 7));
            ToggleCommsLog = map.AddAction("ToggleCommsLog", InputActionType.Button, "<Keyboard>/c");
            TimeFaster = map.AddAction("TimeFaster", InputActionType.Button, "<Keyboard>/period");
            TimeSlower = map.AddAction("TimeSlower", InputActionType.Button, "<Keyboard>/comma");
        }

        public void Enable() { map.Enable(); }
        public void Disable() { map.Disable(); }
        public void Dispose() { map.Dispose(); }
    }
}
