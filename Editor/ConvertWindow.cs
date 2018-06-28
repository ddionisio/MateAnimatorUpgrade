using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace M8.Animator.Upgrade {
    public class ConvertWindow : EditorWindow {
        [System.Flags]
        public enum ConvertFlags {
            None = 0,
            Assets = 1,
            CurrentScene = 2,
            AllScenes = 4,
        }

        public struct Message {
            public string text;
            public Color clr;

            public Message(string text) {
                this.text = text;
                clr = Color.black;
            }

            public Message(string text, Color clr) {
                this.text = text;
                this.clr = clr;
            }
        }

        public struct AnimatorAssetInfo {
            public string guid;
            public string path;
            public AnimatorData animData;
        }

        private List<Message> mMessages;

        private Vector2 mScrollPos;
        
        private Texture2D mTextureBlank;

        private IEnumerator mConvertRout;

        private Dictionary<string, string> mGUIDMetaMatch; //match AnimatorMeta to AnimateMeta GUID

        private bool mIsRemoveOldAnimatorComp;

        [MenuItem("M8/Animator Convert Old")]
        static void Init() {
            EditorWindow.GetWindow(typeof(ConvertWindow), false, "Animator Convert Old");
        }

        void OnDestroy() {
            DestroyImmediate(mTextureBlank);
        }

        void Awake() {
            mTextureBlank = new Texture2D(1, 1);
        }

        void OnEnable() {
            mScrollPos = Vector2.zero;

            mMessages = new List<Message>();

            mGUIDMetaMatch = new Dictionary<string, string>();
        }

        void OnGUI() {
            var messageBkgrndColors = new Color[] { new Color(0.7f, 0.7f, 0.7f), new Color(0.5f, 0.5f, 0.5f) };

            var messageStyle = new GUIStyle(GUI.skin.label);
            messageStyle.normal.background = mTextureBlank;
            messageStyle.wordWrap = true;

            bool defaultEnabled = GUI.enabled;
            var defaultColor = GUI.color;
            var defaultContentColor = GUI.contentColor;
            var defaultBkgrndColor = GUI.backgroundColor;

            GUILayout.Label("Messages");

            mScrollPos = GUILayout.BeginScrollView(mScrollPos, GUI.skin.box);

            for(int i = 0; i < mMessages.Count; i++) {
                GUI.contentColor = mMessages[i].clr;
                GUI.backgroundColor = messageBkgrndColors[i % messageBkgrndColors.Length];

                GUILayout.Label(mMessages[i].text, messageStyle);
            }

            GUI.contentColor = defaultContentColor;
            GUI.backgroundColor = defaultBkgrndColor;

            GUILayout.EndScrollView();

            GUILayout.Space(4f);

            GUI.enabled = mConvertRout == null;

            bool doConvert = false;
            ConvertFlags convertFlags = ConvertFlags.None;

            mIsRemoveOldAnimatorComp = GUILayout.Toggle(mIsRemoveOldAnimatorComp, new GUIContent("Remove Old Animator Component", "If toggled, will remove AnimatorData component, leave it in place otherwise."));

            if(GUILayout.Button("Convert From Assets")) {
                doConvert = true;
                convertFlags = ConvertFlags.Assets;
            }

            if(GUILayout.Button("Convert From All Scenes")) {
                doConvert = true;
                convertFlags = ConvertFlags.AllScenes;
            }

            if(GUILayout.Button("Convert From Current Scene")) {
                doConvert = true;
                convertFlags = ConvertFlags.CurrentScene;
            }

            if(GUILayout.Button("Convert All")) {
                doConvert = true;
                convertFlags = ConvertFlags.Assets | ConvertFlags.AllScenes;
            }
                        
            GUI.enabled = defaultEnabled;

            if(doConvert) {
                if(UnityEditor.EditorUtility.DisplayDialog("Convert", "This will go through and convert AnimatorData to Animate. Make sure to go through your scripts and update the references.", "Proceed")) {
                    mConvertRout = DoConvert(convertFlags, mIsRemoveOldAnimatorComp);
                }
            }
        }

        void Update() {
            if(mConvertRout != null) {
                if(!mConvertRout.MoveNext()) {
                    mConvertRout = null;
                    Repaint();
                }
            }
        }

        IEnumerator DoConvert(ConvertFlags flags, bool removeOldReference) {
            var prefabGUIDs = AssetDatabase.FindAssets("t:Prefab");

            //convert Meta Animators, record its GUID match for later
            mGUIDMetaMatch.Clear();

            //convert from Asset
            if((flags & ConvertFlags.Assets) != ConvertFlags.None) {
                AddMessage("Grabbing Animators from Assets...");
                yield return null;

                //Grab prefabs with AnimatorData
                var animDataList = new List<AnimatorAssetInfo>();
                                
                for(int i = 0; i < prefabGUIDs.Length; i++) {
                    var path = AssetDatabase.GUIDToAssetPath(prefabGUIDs[i]);
                    var animData = AssetDatabase.LoadAssetAtPath<AnimatorData>(path);
                    if(animData) {
                        //AddMessage(path);

                        UnityEditor.EditorUtility.SetDirty(animData.gameObject);

                        animDataList.Add(new AnimatorAssetInfo() { guid=prefabGUIDs[i], path=path, animData=animData });
                    }
                }

                //Go through and convert animators
                for(int i = 0; i < animDataList.Count; i++)
                    yield return DoConvertAnimator(animDataList[i].animData, animDataList[i].guid, animDataList[i].path);
            }

            //convert from all scenes

            //convert from current scene


            //record each animator reference id to rehook later

            //convert animators

            //go through each scene and grab animators, convert

            //

            yield return null;
        }

        IEnumerator DoConvertAnimatorMeta(AnimatorMeta animMeta, string guid, string path) {
            yield return null;
        }

        IEnumerator DoConvertAnimator(AnimatorData animData, string guid, string path) {
            if(!string.IsNullOrEmpty(path))
                AddMessage("Converting Animator: " + path);
            else
                AddMessage("Converting Animator: " + animData.name);

            yield return null;

            var go = animData.gameObject;

            Animate newAnim;

            newAnim = go.AddComponent<Animate>();

            var newAnimTarget = newAnim as ITarget;
            var oldAnimTarget = animData as AMITarget;

            //fill in common data
            newAnim.sequenceLoadAll = animData.sequenceLoadAll;
            newAnim.sequenceKillWhenDone = animData.sequenceKillWhenDone;
            newAnim.playOnEnable = animData.playOnEnable;
            newAnim.isGlobal = animData.isGlobal;
            newAnim.onDisableAction = (Animate.DisableAction)((int)animData.onDisableAction);
            newAnim.updateType = animData.updateType;
            newAnim.updateTimeIndependent = animData.updateTimeIndependent;

            if(oldAnimTarget.isMeta) {
                //grab matching meta for new anim., no need to construct takes
                AddMessage("TODO");
            }
            else {
                //construct and convert takes
            }

            newAnim.defaultTakeName = animData.defaultTakeName;

        }

        IEnumerator DoConvertTake(AMTakeData oldTake, Take newTake) {
            yield return null;
        }
        
        private void AddMessage(string text) {
            mMessages.Add(new Message(text));
        }

        private void AddMessage(string text, Color clr) {
            mMessages.Add(new Message(text, clr));
        }
    }
}
 