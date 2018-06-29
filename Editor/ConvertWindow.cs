using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using DG.Tweening;

namespace M8.Animator.Upgrade {
    public class ConvertWindow : EditorWindow {
        [System.Flags]
        public enum ConvertFlags {
            None = 0,
            Assets = 1,
            Loaded = 2,
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
            public GameObject rootGO;
            public AnimatorData animData;
        }

        public struct MetaInfo {
            public string guid;
            public string path;
            public AnimateMeta meta;
        }

        private List<Message> mMessages;

        private Vector2 mScrollPos;
        
        private Texture2D mTextureBlank;
        
        private bool mIsConverting;

        private Dictionary<string, MetaInfo> mGUIDMetaMatch; //match AnimatorMeta to AnimateMeta GUID

        private bool mIsRemoveOldAnimatorComp = true;

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

            mGUIDMetaMatch = new Dictionary<string, MetaInfo>();
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

            GUI.enabled = !mIsConverting;

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

            if(GUILayout.Button("Convert From Loaded Objects")) {
                doConvert = true;
                convertFlags = ConvertFlags.Loaded;
            }

            if(GUILayout.Button("Convert All")) {
                doConvert = true;
                convertFlags = ConvertFlags.Assets | ConvertFlags.AllScenes;
            }
                        
            GUI.enabled = defaultEnabled;

            if(doConvert) {
                if(UnityEditor.EditorUtility.DisplayDialog("Convert", "This will go through and convert AnimatorData to Animate. Make sure to go through your scripts and update the references.", "Proceed")) {
                    EditorCoroutines.StartCoroutine(DoConvert(convertFlags, mIsRemoveOldAnimatorComp), this);
                }
            }
        }

        IEnumerator DoConvert(ConvertFlags flags, bool removeOldReference) {
            mIsConverting = true;

            var prefabGUIDs = AssetDatabase.FindAssets("t:Prefab");

            //convert Meta Animators, record its GUID match for later
            for(int i = 0; i < prefabGUIDs.Length; i++) {
                var guid = prefabGUIDs[i];

                MetaInfo metaInfo;
                if(mGUIDMetaMatch.TryGetValue(guid, out metaInfo)) { //already processed from before
                    //make sure meta still exists
                    if(metaInfo.meta)
                        continue;
                    else { //recreate
                        mGUIDMetaMatch.Remove(guid);
                    }
                }

                var path = AssetDatabase.GUIDToAssetPath(prefabGUIDs[i]);

                var animMeta = AssetDatabase.LoadAssetAtPath<AnimatorMeta>(path);
                if(animMeta) {
                    //check if it already has a converted asset
                    string metaConvertPath = GetNewMetaPath(path);
                    string metaConvertGUID = AssetDatabase.AssetPathToGUID(metaConvertPath);
                    if(!string.IsNullOrEmpty(metaConvertGUID)) {
                        //add to meta match
                        var meta = AssetDatabase.LoadAssetAtPath<AnimateMeta>(metaConvertPath);
                        if(meta) { //if null, need to recreate
                            mGUIDMetaMatch.Add(guid, new MetaInfo() { guid = metaConvertGUID, path = metaConvertPath, meta = meta });
                            continue;
                        }
                    }

                    //convert
                    var newMeta = ScriptableObject.CreateInstance<AnimateMeta>();

                    AddMessage("Creating new AnimateMeta: " + metaConvertPath);
                    yield return new WaitForFixedUpdate();

                    yield return EditorCoroutines.StartCoroutine(DoConvertAnimatorMeta(animMeta, newMeta), this);

                    AssetDatabase.CreateAsset(newMeta, metaConvertPath);
                    AssetDatabase.SaveAssets();

                    mGUIDMetaMatch.Add(guid, new MetaInfo() { guid = AssetDatabase.AssetPathToGUID(metaConvertPath), path = metaConvertPath, meta = newMeta });
                }
            }

            //convert from Asset
            if((flags & ConvertFlags.Assets) != ConvertFlags.None) {
                AddMessage("Grabbing Animators from Assets...");
                yield return new WaitForFixedUpdate();

                //Grab prefabs with AnimatorData
                var animDataList = new List<AnimatorAssetInfo>();

                for(int i = 0; i < prefabGUIDs.Length; i++) {
                    var path = AssetDatabase.GUIDToAssetPath(prefabGUIDs[i]);
                    var animData = AssetDatabase.LoadAssetAtPath<AnimatorData>(path);
                    if(animData) {
                        //AddMessage(path);

                        //exclude ones with Animate on it, assume it is already converted
                        var animate = animData.GetComponent<Animate>();
                        if(animate)
                            continue;

                        var rootGO = animData.transform.root.gameObject;

                        UnityEditor.EditorUtility.SetDirty(rootGO);

                        animDataList.Add(new AnimatorAssetInfo() { guid = prefabGUIDs[i], path = path, animData = animData, rootGO = rootGO });
                    }
                }

                //Go through and convert animators
                for(int i = 0; i < animDataList.Count; i++) {
                    AddMessage("Converting Animator: " + animDataList[i].path);
                    yield return new WaitForFixedUpdate();

                    yield return EditorCoroutines.StartCoroutine(DoConvertAnimator(animDataList[i].animData), this);

                    //delete old animator?
                    if(removeOldReference) {
                        Object.DestroyImmediate(animDataList[i].animData, true);
                    }
                }
            }

            if((flags & ConvertFlags.AllScenes) != ConvertFlags.None) { //convert from all scenes
                //UnityEngine.SceneManagement.SceneManager.GetAllScenes();

                var sceneGUIDs = AssetDatabase.FindAssets("t:Scene");
                for(int i = 0; i < sceneGUIDs.Length; i++) {
                    var scenePath = AssetDatabase.GUIDToAssetPath(sceneGUIDs[i]);

                    AddMessage("Loading scene: " + scenePath);

                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

                    yield return new WaitForSeconds(0.3f);

                    yield return EditorCoroutines.StartCoroutine(DoConvertFromLoadedScenes(removeOldReference), this);

                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

                    yield return new WaitForSeconds(0.3f);
                }
            }
            else if((flags & ConvertFlags.Loaded) != ConvertFlags.None) { //convert from current scene
                yield return EditorCoroutines.StartCoroutine(DoConvertFromLoadedScenes(removeOldReference), this);
            }
            
            mIsConverting = false;
            Repaint();
        }

        IEnumerator DoConvertFromLoadedScenes(bool removeOldReference) {
            AddMessage("Grabbing Animators from loaded objects...");
            yield return new WaitForFixedUpdate();

            var animDatas = Resources.FindObjectsOfTypeAll<AnimatorData>();

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            //go through and convert
            for(int i = 0; i < animDatas.Length; i++) {
                var go = animDatas[i].gameObject;

                if(go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave)
                    continue;

                //if(UnityEditor.EditorUtility.IsPersistent(go.transform.root.gameObject)) //exclude prefab instance
                //  continue;

                //exclude ones with Animate on it, assume it is already converted
                var animate = go.GetComponent<Animate>();
                if(animate)
                    continue;

                AddMessage("Converting Animator: " + animDatas[i].name);
                yield return new WaitForFixedUpdate();

                yield return EditorCoroutines.StartCoroutine(DoConvertAnimator(animDatas[i]), this);

                //delete old animator?
                if(removeOldReference) {
                    Object.DestroyImmediate(animDatas[i]);
                }
            }
        }

        IEnumerator DoConvertAnimatorMeta(AnimatorMeta oldMeta, AnimateMeta newMeta) {
            foreach(var oldTake in oldMeta.takes) {
                var newTake = new Take();

                AddMessage(" - convert take: " + oldTake.name);

                yield return EditorCoroutines.StartCoroutine(DoConvertTake(oldTake, newTake, true), this);

                newMeta.takes.Add(newTake);
            }
        }

        IEnumerator DoConvertAnimator(AnimatorData animData) {
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
                var oldMetaTarget = animData as AMIMeta;
                var oldMetaPath = AssetDatabase.GetAssetPath(oldMetaTarget.meta);
                var oldMetaGUID = AssetDatabase.AssetPathToGUID(oldMetaPath);

                //grab matching meta for new anim., no need to construct takes
                MetaInfo metaInfo;
                if(mGUIDMetaMatch.TryGetValue(oldMetaGUID, out metaInfo)) {
                    newAnimTarget.meta = metaInfo.meta;
                }
                else
                    AddMessage("Unable to find matching meta for: " + oldMetaPath, Color.yellow);
            }
            else {
                //construct and convert takes
                foreach(var oldTake in oldAnimTarget.takes) {
                    var newTake = new Take();

                    AddMessage(" - convert take: " + oldTake.name);

                    yield return EditorCoroutines.StartCoroutine(DoConvertTake(oldTake, newTake, false), this);

                    newAnimTarget.takes.Add(newTake);
                }
            }

            newAnim.defaultTakeName = animData.defaultTakeName;
        }

        IEnumerator DoConvertTake(AMTakeData oldTake, Take newTake, bool isMeta) {
            newTake.name = oldTake.name;
            newTake.frameRate = oldTake.frameRate;
            newTake.endFramePadding = oldTake.endFramePadding;
            newTake.numLoop = oldTake.numLoop;
            newTake.loopMode = oldTake.loopMode;
            newTake.loopBackToFrame = oldTake.loopBackToFrame;
            newTake.trackCounter = oldTake.track_count;
            newTake.groupCounter = oldTake.group_count;

            //go through groups
            newTake.rootGroup = new Group();
            newTake.rootGroup.group_name = oldTake.rootGroup.group_name;
            newTake.rootGroup.group_id = oldTake.rootGroup.group_id;
            newTake.rootGroup.elements = new List<int>(oldTake.rootGroup.elements);
            newTake.rootGroup.foldout = oldTake.rootGroup.foldout;

            newTake.groupValues = new List<Group>();
            foreach(var oldGroup in oldTake.groupValues) {
                var newGroup = new Group();
                newGroup.group_name = oldGroup.group_name;
                newGroup.group_id = oldGroup.group_id;
                newGroup.elements = new List<int>(oldGroup.elements);
                newGroup.foldout = oldGroup.foldout;

                newTake.groupValues.Add(newGroup);
            }

            //go through tracks
            newTake.trackValues = new List<Track>();
            foreach(var oldTrack in oldTake.trackValues) {
                AddMessage("  - convert track: " + oldTrack.name);

                Track newTrack = null;

                if(oldTrack is AMAnimationTrack) {

                }
                else if(oldTrack is AMAudioTrack) {

                }
                else if(oldTrack is AMCameraSwitcherTrack) {

                }
                else if(oldTrack is AMEventTrack) {

                }
                else if(oldTrack is AMGOSetActiveTrack) {

                }
                else if(oldTrack is AMMaterialTrack) {

                }
                else if(oldTrack is AMOrientationTrack) {

                }
                else if(oldTrack is AMPropertyTrack) {

                }
                else if(oldTrack is AMRotationEulerTrack) {

                }
                else if(oldTrack is AMRotationTrack) {
                    newTrack = new RotationTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, isMeta);
                                        
                    newTrack.keys = new List<Key>();

                    foreach(AMRotationKey oldKey in oldTrack.keys) {
                        var newKey = new RotationKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.rotation = oldKey.rotation;
                        newKey.endFrame = oldKey.endFrame;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMTranslationTrack) {
                    newTrack = new TranslationTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, isMeta);
                    
                    newTrack.keys = new List<Key>();

                    foreach(AMTranslationKey oldKey in oldTrack.keys) {
                        var newKey = new TranslationKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.position = oldKey.position;
                        newKey.endFrame = oldKey.endFrame;
                        newKey.isConstSpeed = oldKey.isConstSpeed;

                        newKey.path = new Vector3[oldKey.path.Length];
                        System.Array.Copy(oldKey.path, newKey.path, newKey.path.Length);

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMTriggerTrack) {

                }

                newTake.trackValues.Add(newTrack);

                yield return new WaitForFixedUpdate();
            }
        }

        void ConvertTrackCommonFields(AMTrack oldTrack, Track newTrack, bool isMeta) {
            newTrack.id = oldTrack.id;
            newTrack.name = oldTrack.name;
            newTrack.foldout = oldTrack.foldout;

            if(isMeta)
                newTrack.SetTargetDirect(null, oldTrack.targetPath);
            else
                newTrack.SetTargetDirect(oldTrack.GetTarget(null), "");
        }

        void ConvertKeyCommonFields(AMKey oldKey, Key newKey) {
            newKey.version = oldKey.version;
            newKey.interp = (Key.Interpolation)oldKey.interp;
            newKey.frame = oldKey.frame;
            newKey.easeType = (Ease)oldKey.easeType;
            newKey.amplitude = oldKey.amplitude;
            newKey.period = oldKey.period;
            newKey.customEase = new List<float>(oldKey.customEase);
        }

        string GetNewMetaPath(string oldMetaPath) {
            int extInd = oldMetaPath.LastIndexOf('.');
            if(extInd == -1)
                return oldMetaPath + ".asset";
            else
                return oldMetaPath.Substring(0, extInd) + ".asset";
        }

        private void AddMessage(string text) {
            //Debug.Log(text);
            mMessages.Add(new Message(text));
        }

        private void AddMessage(string text, Color clr) {
            mMessages.Add(new Message(text, clr));
        }
    }
}
 
 
 