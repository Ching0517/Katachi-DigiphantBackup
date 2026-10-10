using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DigiPhant
{
    public enum SignalCombination { Sum, Average }
    [Serializable] public class MovementSource
    {
        [Range(1, 4)] public int performer = 1;
        public Movement movement;
        [Range(-2, 2)] public float weight = 1;
    }
    [Serializable] public class MovementDriver
    {
        public SignalCombination combination = SignalCombination.Average;
        public MovementSource[] sources = Array.Empty<MovementSource>();
        [Range(0, 3)] public float sensitivity = 1;
        [Range(0, .5f)] public float deadZone = .12f;
        public bool TryRead(DigiPhantController controller, float now, out float value)
        {
            value = 0;
            float totalWeight = 0;
            foreach (var source in sources)
            {
                if (source == null || Mathf.Abs(source.weight) < .0001f) continue;
                if (!controller.TryReadMovement(source.performer, source.movement, now, out float input)) return false;
                value += input * source.weight;
                totalWeight += Mathf.Abs(source.weight);
            }
            if (totalWeight == 0) return true; // An empty mapping leaves this channel at zero.
            if (combination == SignalCombination.Average) value /= totalWeight;
            value = Mathf.Clamp(value * sensitivity, -1, 1);
            value = Mathf.Abs(value) <= deadZone ? 0 : Mathf.Sign(value) * Mathf.InverseLerp(deadZone, 1, Mathf.Abs(value));
            return true;
        }
    }

    public class DigiPhantLocomotion : MonoBehaviour
    {
        public bool enableLocomotion = true;
        public Transform travelRoot;
        public Animator elephantAnimator;
        public Transform animationMotionRoot;
        public Camera followCamera;
        public bool followElephant = true;
        public AnimationClip idle, walk, run, backward, turnLeft, turnRight, walkLeft, walkRight, runLeft, runRight;
        [Header("Gesture actions")]
        public bool enableGestureActions = true;
        [Tooltip("The one-shot vendor clip played by a Victory gesture.")]
        public AnimationClip drink;
        [Range(.05f, 1)] public float gestureBlendSeconds = .2f;
        [Range(-1, 1)] public float thumbUpHeadValue = -1;
        [Range(-1, 1)] public float thumbDownHeadValue = 1;
        [Range(-1, 1)] public float closedFistTrunkValue = 1;
        public MovementDriver forward = new MovementDriver { sources = new[] { new MovementSource { movement = Movement.LeftHandHeight } } };
        public MovementDriver steering = new MovementDriver { sources = new[] { new MovementSource { movement = Movement.Lean } } };
        [Min(.01f)] public float walkSpeed = 1.8f;
        [Min(.01f)] public float runSpeed = 4.5f;
        [Min(.01f)] public float backwardSpeed = 1.2f;
        [Min(0)] public float turnDegreesPerSecond = 60;
        [Range(.2f, .95f)] public float runThreshold = .65f;
        [Min(.01f)] public float animationBlendSeconds = .25f;
        [Range(0, 20)] public float inputSmoothing = 6;
        [Tooltip("Maximum travel distance from the starting point, in Unity units. Zero disables the limit.")]
        [Min(0)] public float stageRadius = 15;
        [Range(-1, 1)] public float testForward;
        [Range(-1, 1)] public float testSteering;
        public string CurrentAction { get; private set; } = "Idle";
        public string GestureStatus { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool AnimationActive => graph.IsValid();
        public bool IsDrinkActive => enableLocomotion && (drinkWeight > .001f || drinkRequested);
        public float DrinkWeight => Mathf.Clamp01(drinkWeight);
        public float BoneOverlayWeight => 1 - DrinkWeight;
        public float AnimationWeightSum { get; private set; }
        bool GestureActionsActive => enableGestureActions && controller != null &&
            controller.enableGestureActions && controller.inputMode == InputMode.Camera;
        DigiPhantController controller;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        AnimationClip graphDrink;
        float[] gaitWeights;
        readonly string[] actions = { "Idle", "Walk", "Run", "Backward", "Turn left", "Turn right", "Walk left", "Walk right", "Run left", "Run right" };
        readonly Dictionary<Transform, Pose> restPose = new Dictionary<Transform, Pose>();
        Vector3 startPosition, cameraOffset, cameraStart, motionPosition;
        Quaternion startRotation, motionRotation;
        bool initialised;
        bool previousAnimatorEnabled, previousRootMotion;
        AnimatorCullingMode previousCulling;
        RuntimeAnimatorController previousController;
        float throttle, turn, cycle;
        string activeGesture = "None";
        float headGestureValue, trunkGestureValue, drinkWeight, drinkTime;
        bool drinkRequested, victoryLatched;

        void OnEnable() { controller = GetComponent<DigiPhantController>(); }
        public void SetGesture(string gesture, bool freshSnapshot)
        {
            if (!GestureActionsActive || !freshSnapshot)
            {
                activeGesture = "None";
                drinkRequested = false;
                return;
            }
            activeGesture = gesture ?? "None";
            if (activeGesture != "Victory") victoryLatched = false;
            if (activeGesture == "Victory" && !victoryLatched)
            {
                victoryLatched = true;
                if (!enableLocomotion) GestureStatus = "Locomotion is disabled; drink action is unavailable.";
                else if (drink != null) { drinkRequested = true; drinkTime = 0; GestureStatus = null; }
                else GestureStatus = "Drink clip is not assigned; continuing locomotion.";
            }
            if (activeGesture != "Victory") drinkRequested = false;
        }
        public void ClearGestureActions()
        {
            activeGesture = "None";
            headGestureValue = trunkGestureValue = 0;
            drinkWeight = drinkTime = 0;
            drinkRequested = victoryLatched = false;
            GestureStatus = null;
        }
        public bool ShouldGestureOwn(string controlLabel)
        {
            return GestureActionsActive && (controlLabel == "Head turn" || controlLabel == "Trunk curl");
        }
        public float GestureValueFor(string controlLabel)
        {
            if (!GestureActionsActive) return 0;
            return controlLabel == "Head turn" ? headGestureValue : controlLabel == "Trunk curl" ? trunkGestureValue : 0;
        }
        public bool Initialise()
        {
            if (initialised) return true;
            if (!travelRoot || !elephantAnimator || !animationMotionRoot || !idle || !walk || !run || !backward || !turnLeft || !turnRight)
            { CurrentAction = "Locomotion setup incomplete"; return false; }
            controller = GetComponent<DigiPhantController>();
            startPosition = travelRoot.position;
            startRotation = travelRoot.rotation;
            if (followCamera) { cameraStart = followCamera.transform.position; cameraOffset = cameraStart - startPosition; }
            motionPosition = animationMotionRoot.localPosition;
            motionRotation = animationMotionRoot.localRotation;
            foreach (var bone in elephantAnimator.GetComponentsInChildren<Transform>())
                restPose[bone] = new Pose(bone.localPosition, bone.localRotation);
            initialised = true;
            return true;
        }
        bool StartAnimation()
        {
            if (graph.IsValid()) return true;
            if (!Initialise()) return false;
            previousAnimatorEnabled = elephantAnimator.enabled;
            previousRootMotion = elephantAnimator.applyRootMotion;
            previousCulling = elephantAnimator.cullingMode;
            previousController = elephantAnimator.runtimeAnimatorController;
            elephantAnimator.runtimeAnimatorController = null;
            elephantAnimator.applyRootMotion = false;
            elephantAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            elephantAnimator.enabled = true;
            graph = PlayableGraph.Create("DigiPhant locomotion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clips = new[] { idle, walk, run, backward, turnLeft, turnRight, walkLeft ? walkLeft : walk,
                walkRight ? walkRight : walk, runLeft ? runLeft : run, runRight ? runRight : run };
            mixer = AnimationMixerPlayable.Create(graph, clips.Length + (drink ? 1 : 0));
            playables = new AnimationClipPlayable[mixer.GetInputCount()];
            gaitWeights = new float[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == 0 ? 1 : 0);
                gaitWeights[i] = i == 0 ? 1 : 0;
            }
            if (drink)
            {
                int drinkIndex = clips.Length;
                playables[drinkIndex] = AnimationClipPlayable.Create(graph, drink);
                playables[drinkIndex].SetApplyFootIK(false);
                graph.Connect(playables[drinkIndex], 0, mixer, drinkIndex);
                mixer.SetInputWeight(drinkIndex, 0);
            }
            graphDrink = drink;
            var output = AnimationPlayableOutput.Create(graph, "Elephant gait", elephantAnimator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            return true;
        }
        public bool InputsVisible(float now)
        {
            if (!enableLocomotion) return true;
            if (controller == null) controller = GetComponent<DigiPhantController>();
            foreach (var driver in new[] { forward, steering })
                foreach (var source in driver.sources)
                    if (source != null && Mathf.Abs(source.weight) > .0001f &&
                        !controller.IsMovementVisible(source.performer, source.movement, now)) return false;
            return true;
        }
        public void ClampPerformers(int count)
        {
            foreach (var driver in new[] { forward, steering })
                foreach (var source in driver.sources)
                    if (source != null) source.performer = Mathf.Clamp(source.performer, 1, count);
            StopMotion();
        }
        public void StopMotion()
        {
            testForward = testSteering = throttle = turn = CurrentSpeed = 0;
            CurrentAction = "Idle";
        }
        public bool Evaluate(float now, float dt)
        {
            dt = Mathf.Clamp(dt, 0, .1f);
            float gestureBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, gestureBlendSeconds));
            float desiredHead = activeGesture == "Thumb_Up" ? thumbUpHeadValue :
                activeGesture == "Thumb_Down" ? thumbDownHeadValue : 0;
            float desiredTrunk = activeGesture == "Closed_Fist" ? closedFistTrunkValue : 0;
            headGestureValue = Mathf.Lerp(headGestureValue, desiredHead, gestureBlend);
            trunkGestureValue = Mathf.Lerp(trunkGestureValue, desiredTrunk, gestureBlend);
            if (!enableLocomotion)
            {
                drinkRequested = false;
                drinkWeight = 0;
                StopAnimation();
                return false;
            }
            // The inspector can change this reference while the graph is live.
            // Rebuild before indexing its optional drink input.
            if (graph.IsValid() && graphDrink != drink) StopAnimation();
            if (!StartAnimation())
            {
                drinkRequested = false;
                drinkWeight = 0;
                GestureStatus = "Locomotion setup is incomplete; drink action is unavailable.";
                return false;
            }
            if (!drink && drinkRequested)
            {
                drinkRequested = false;
                drinkWeight = 0;
                GestureStatus = "Drink clip is not assigned; continuing locomotion.";
            }
            else if (drinkRequested && drinkTime >= drink.length) drinkRequested = false;
            drinkWeight = Mathf.Lerp(drinkWeight, drinkRequested ? 1 : 0, gestureBlend);
            if (drink)
            {
                int drinkIndex = clips.Length;
                playables[drinkIndex].SetTime(Mathf.Min(drinkTime, Mathf.Max(0, drink.length - .0001f)));
                mixer.SetInputWeight(drinkIndex, DrinkWeight);
                if (drinkRequested) drinkTime += dt;
            }
            float desiredForward = testForward, desiredTurn = testSteering;
            bool valid = true;
            if (controller.inputMode == InputMode.Camera)
            {
                bool moving = forward.TryRead(controller, now, out desiredForward);
                bool turning = steering.TryRead(controller, now, out desiredTurn);
                valid = moving && turning;
            }
            if (!valid || IsDrinkActive) { throttle = turn = 0; }
            else
            {
                float blend = inputSmoothing <= 0 ? 1 : 1 - Mathf.Exp(-inputSmoothing * dt);
                throttle = Mathf.Lerp(throttle, Mathf.Clamp(desiredForward, -1, 1), blend);
                turn = Mathf.Lerp(turn, Mathf.Clamp(desiredTurn, -1, 1), blend);
            }
            if (Mathf.Abs(throttle) < .015f) throttle = 0;
            if (Mathf.Abs(turn) < .015f) turn = 0;
            bool running = throttle > runThreshold;
            CurrentSpeed = throttle < 0 ? throttle * backwardSpeed : throttle <= runThreshold
                ? throttle / runThreshold * walkSpeed : Mathf.Lerp(walkSpeed, runSpeed, Mathf.InverseLerp(runThreshold, 1, throttle));
            int action = throttle < 0 ? 3 : throttle > 0 ? (running ? 2 : 1) : turn < 0 ? 4 : turn > 0 ? 5 : 0;
            if (throttle > 0 && Mathf.Abs(turn) > .15f) action = running ? (turn < 0 ? 8 : 9) : (turn < 0 ? 6 : 7);
            CurrentAction = IsDrinkActive ? "Drink water" : actions[action];
            float animationRate = action == 0 ? 1 : throttle != 0
                ? Mathf.Max(.15f, Mathf.Abs(CurrentSpeed) / (throttle < 0 ? backwardSpeed : running ? runSpeed : walkSpeed))
                : Mathf.Max(.15f, Mathf.Abs(turn) * turnDegreesPerSecond / 18.75f);
            cycle += dt * animationRate / Mathf.Max(.01f, clips[action].length);
            cycle = Mathf.Repeat(cycle, 1);
            float weightBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, animationBlendSeconds));
            for (int i = 0; i < clips.Length; i++)
            {
                gaitWeights[i] = Mathf.Lerp(gaitWeights[i], i == action ? 1 : 0, weightBlend);
                mixer.SetInputWeight(i, gaitWeights[i] * (1 - DrinkWeight));
                playables[i].SetTime(cycle * clips[i].length);
            }
            AnimationWeightSum = 1 - DrinkWeight;
            if (drink) AnimationWeightSum += mixer.GetInputWeight(clips.Length);
            graph.Evaluate(0);
            // The travel parent alone owns world movement. Remove exported motion-node drift.
            animationMotionRoot.localPosition = motionPosition;
            animationMotionRoot.localRotation = motionRotation;
            travelRoot.Rotate(Vector3.up, turn * turnDegreesPerSecond * dt, Space.World);
            Vector3 destination = travelRoot.position + travelRoot.forward * CurrentSpeed * dt;
            if (stageRadius > 0)
            {
                Vector3 offset = destination - startPosition;
                offset.y = 0;
                if (offset.magnitude > stageRadius)
                {
                    destination = startPosition + Vector3.ClampMagnitude(offset, stageRadius);
                    CurrentAction += " · stage edge";
                }
            }
            destination.y = startPosition.y;
            travelRoot.position = destination;
            if (followElephant && followCamera) followCamera.transform.position = destination + cameraOffset;
            return true;
        }
        public void ResetPosition()
        {
            if (!Initialise()) return;
            StopMotion();
            travelRoot.SetPositionAndRotation(startPosition, startRotation);
            if (followCamera) followCamera.transform.position = cameraStart;
            controller.InvalidateCalibration();
        }
        public void StopAnimation()
        {
            if (!graph.IsValid()) return;
            graph.Destroy();
            graphDrink = null;
            if (elephantAnimator)
            {
                elephantAnimator.runtimeAnimatorController = previousController;
                elephantAnimator.applyRootMotion = previousRootMotion;
                elephantAnimator.cullingMode = previousCulling;
                elephantAnimator.enabled = previousAnimatorEnabled;
            }
            foreach (var pair in restPose)
                if (pair.Key) { pair.Key.localPosition = pair.Value.position; pair.Key.localRotation = pair.Value.rotation; }
            StopMotion();
        }
        void OnDisable()
        {
            ClearGestureActions();
            StopAnimation();
            if (initialised)
            {
                if (travelRoot) travelRoot.SetPositionAndRotation(startPosition, startRotation);
                if (followCamera) followCamera.transform.position = cameraStart;
            }
            initialised = false;
            restPose.Clear();
        }
        public void DrawControls(float width)
        {
            var label = new GUIStyle(GUI.skin.label) { wordWrap = true };
            bool selected = GUILayout.Toggle(enableLocomotion, "Enable locomotion");
            if (selected != enableLocomotion) { enableLocomotion = selected; StopMotion(); ClearGestureActions(); }
            if (!enableLocomotion) return;
            GUILayout.Label(CurrentAction + " | " + Mathf.Abs(CurrentSpeed).ToString("0.0") + " units/s", label, GUILayout.Width(width));
            if (controller.inputMode == InputMode.TestSliders)
            {
                GUILayout.Label("Travel: backward / stop / forward", label, GUILayout.Width(width));
                testForward = GUILayout.HorizontalSlider(testForward, -1, 1);
                GUILayout.Label("Turn: left / straight / right", label, GUILayout.Width(width));
                testSteering = GUILayout.HorizontalSlider(testSteering, -1, 1);
                if (GUILayout.Button("Stop moving")) StopMotion();
            }
            else GUILayout.Label("Speed and steering use the movement mappings in the Inspector.", label, GUILayout.Width(width));
            if (GUILayout.Button("Return to starting position")) ResetPosition();
            GUILayout.Space(8);
        }
    }
}
