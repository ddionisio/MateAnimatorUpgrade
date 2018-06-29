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
        private Dictionary<string, TriggerSignal> mTriggerSignalLookup; //key = asset path

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
            mTriggerSignalLookup = new Dictionary<string, TriggerSignal>();
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

            GUILayout.Space(8f);

            GUI.enabled = !mIsConverting;

            bool doConvert = false;
            ConvertFlags convertFlags = ConvertFlags.None;

            bool doDeleteOldMetas = false;

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

            GUILayout.Space(8f);

            if(GUILayout.Button(new GUIContent("Delete All AnimatorMeta")))
                doDeleteOldMetas = true;
                        
            GUI.enabled = defaultEnabled;

            if(doConvert) {
                if(UnityEditor.EditorUtility.DisplayDialog("Convert", "This will go through and convert AnimatorData to Animate. Make sure to go through your scripts and update the references.", "Proceed")) {
                    EditorCoroutines.StartCoroutine(DoConvert(convertFlags, mIsRemoveOldAnimatorComp), this);
                }
            }
            else if(doDeleteOldMetas) {
                if(UnityEditor.EditorUtility.DisplayDialog("Convert", "This will delete all AnimatorMeta in Assets. Only do this AFTER you have converted everything, especially Animators that are hooked with metas.", "Proceed")) {
                    EditorCoroutines.StartCoroutine(DoDeleteMetas(), this);
                }
            }
        }

        IEnumerator DoDeleteMetas() {
            mIsConverting = true;

            var prefabGUIDs = AssetDatabase.FindAssets("t:Prefab");

            for(int i = 0; i < prefabGUIDs.Length; i++) {
                var guid = prefabGUIDs[i];

                var path = AssetDatabase.GUIDToAssetPath(guid);

                var animMeta = AssetDatabase.LoadAssetAtPath<AnimatorMeta>(path);
                if(animMeta) {
                    AddMessage("Deleting: " + path);
                    yield return new WaitForFixedUpdate();

                    AssetDatabase.DeleteAsset(path);
                }
            }

            mIsConverting = false;
            Repaint();
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

                    yield return EditorCoroutines.StartCoroutine(DoConvertAnimatorMeta(animMeta, newMeta, metaConvertPath), this);

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

                    yield return EditorCoroutines.StartCoroutine(DoConvertAnimator(animDataList[i].animData, animDataList[i].path), this);

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

                yield return EditorCoroutines.StartCoroutine(DoConvertAnimator(animDatas[i], animDatas[i].name), this);

                //delete old animator?
                if(removeOldReference) {
                    Object.DestroyImmediate(animDatas[i]);
                }
            }
        }

        IEnumerator DoConvertAnimatorMeta(AnimatorMeta oldMeta, AnimateMeta newMeta, string assetPath) {
            foreach(var oldTake in oldMeta.takes) {
                var newTake = new Take();

                AddMessage(" - convert take: " + oldTake.name);

                yield return EditorCoroutines.StartCoroutine(DoConvertTake(oldTake, newTake, true, assetPath), this);

                newMeta.takes.Add(newTake);
            }
        }

        IEnumerator DoConvertAnimator(AnimatorData animData, string assetPath) {
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

                    yield return EditorCoroutines.StartCoroutine(DoConvertTake(oldTake, newTake, false, assetPath), this);

                    newAnimTarget.takes.Add(newTake);
                }
            }

            newAnim.defaultTakeName = animData.defaultTakeName;
        }

        IEnumerator DoConvertTake(AMTakeData oldTake, Take newTake, bool isMeta, string assetPath) {
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
                    newTrack = new UnityAnimationTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);
                                        
                    newTrack.keys = new List<Key>();
                    foreach(AMAnimationKey oldKey in oldTrack.keys) {
                        var newKey = new UnityAnimationKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.wrapMode = oldKey.wrapMode;
                        newKey.amClip = oldKey.amClip;
                        newKey.crossfade = oldKey.crossfade;
                        newKey.crossfadeTime = oldKey.crossfadeTime;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMAudioTrack) {
                    newTrack = new AudioTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newTrack.keys = new List<Key>();
                    foreach(AMAudioKey oldKey in oldTrack.keys) {
                        var newKey = new AudioKey();
                                                
                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.audioClip = oldKey.audioClip;
                        newKey.loop = oldKey.loop;
                        newKey.oneShot = oldKey.oneShot;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMCameraSwitcherTrack) {
                    newTrack = new CameraSwitcherTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newTrack.keys = new List<Key>();
                    for(int i = 0; i < oldTrack.keys.Count; i++) {
                        var oldKey = (AMCameraSwitcherKey)oldTrack.keys[i];
                        var newKey = new CameraSwitcherKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.type = oldKey.type;
                        newKey.typeEnd = oldKey.typeEnd;
                        newKey.color = oldKey.color;
                        newKey.colorEnd = oldKey.colorEnd;
                        newKey.cameraFadeType = oldKey.cameraFadeType;
                        newKey.cameraFadeParameters = new List<float>(oldKey.cameraFadeParameters);
                        newKey.irisShape = oldKey.irisShape;
                        newKey.still = oldKey.still;
                        newKey.endFrame = oldKey.endFrame;

                        if(isMeta) {
                            newKey.SetCameraDirect(null, oldKey.cameraTargetPath);
                            newKey.SetCameraEndDirect(null, oldKey.cameraEndTargetPath);
                        }
                        else {
                            newKey.SetCameraDirect(oldKey.getCamera(null), "");
                            newKey.SetCameraDirect(oldKey.getCameraEnd(null), "");
                        }

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMEventTrack) {
                    var newEventTrack = new EventTrack();
                    newTrack = newEventTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, false, isMeta);

                    newTrack.keys = new List<Key>();

                    string eventCompName = null;

                    //TODO: create new tracks per different components from keys
                    //for now we only allow conversion of one component, so the first key will be used.
                    foreach(AMEventKey oldKey in oldTrack.keys) {
                        string keyCompName = oldKey.getComponentName();

                        if(string.IsNullOrEmpty(eventCompName)) {
                            if(!string.IsNullOrEmpty(keyCompName)) {
                                eventCompName = keyCompName;

                                AddMessage("   - EventTrack using component: " + eventCompName);

                                if(isMeta)
                                    newEventTrack.SetTargetAsComponentDirect(oldTrack.targetPath, null, eventCompName);
                                else
                                    newEventTrack.SetTargetAsComponentDirect("", oldKey.getComponentRef(), eventCompName);
                            }
                        }

                        //only add if component matched
                        if(string.IsNullOrEmpty(eventCompName) || keyCompName != eventCompName) {
                            AddMessage("   - Cannot add EventKey with Component: " + eventCompName, Color.yellow);
                            continue;
                        }

                        var newKey = new EventKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.useSendMessage = oldKey.useSendMessage;
                        newKey.methodName = oldKey.methodName;

                        newKey.parameters = new List<EventParameter>(oldKey.parameters.Count);
                        for(int i = 0; i < oldKey.parameters.Count; i++) {
                            var oldParm = oldKey.parameters[i];
                            var newParm = new EventParameter();

                            ConvertEventParameter(oldParm, newParm);

                            newKey.parameters.Add(newParm);
                        }

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMGOSetActiveTrack) {
                    var oldGOTrack = (AMGOSetActiveTrack)oldTrack;
                    var newGOTrack = new GOSetActiveTrack();

                    newTrack = newGOTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newGOTrack.startActive = oldGOTrack.startActive;

                    newTrack.keys = new List<Key>();
                    foreach(AMGOSetActiveKey oldKey in oldTrack.keys) {
                        var newKey = new GOSetActiveKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.setActive = oldKey.setActive;
                        newKey.endFrame = oldKey.endFrame;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMMaterialTrack) {
                    var oldMatTrack = (AMMaterialTrack)oldTrack;
                    var newMatTrack = new MaterialTrack();

                    newTrack = newMatTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newMatTrack.materialIndex = oldMatTrack.materialIndex;
                    newMatTrack.property = oldMatTrack.property;
                    newMatTrack.propertyType = (MaterialTrack.ValueType)oldMatTrack.propertyType;

                    newTrack.keys = new List<Key>();
                    foreach(AMMaterialKey oldKey in oldTrack.keys) {
                        var newKey = new MaterialKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.endFrame = oldKey.endFrame;
                        newKey.texture = oldKey.texture;
                        newKey.vector = oldKey.vector;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMOrientationTrack) {
                    newTrack = new OrientationTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newTrack.keys = new List<Key>();
                    foreach(AMOrientationKey oldKey in oldTrack.keys) {
                        var newKey = new OrientationKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        if(isMeta)
                            newKey.SetTargetDirect(null, oldKey.GetTargetPath());
                        else
                            newKey.SetTargetDirect(oldKey.GetTarget(null), "");

                        newKey.endFrame = oldKey.endFrame;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMPropertyTrack) {
                    var oldPropTrack = (AMPropertyTrack)oldTrack;
                    var newPropTrack = new PropertyTrack();

                    newTrack = newPropTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newPropTrack.valueType = (PropertyTrack.ValueType)oldPropTrack.valueType;

                    if(oldPropTrack.isPropertySet()) {
                        Component comp = oldPropTrack.GetTargetComp(null);
                        string compName = oldPropTrack.getComponentName();
                        bool isField = oldPropTrack.isField;
                        string fieldName = oldPropTrack.getMemberName();

                        if(isMeta)
                            newPropTrack.SetTargetCompDirect(null, compName, isField, fieldName);
                        else
                            newPropTrack.SetTargetCompDirect(comp, compName, isField, fieldName);
                    }

                    newTrack.keys = new List<Key>();
                    foreach(AMPropertyKey oldKey in oldTrack.keys) {
                        var newKey = new PropertyKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.endFrame = oldKey.endFrame;
                        newKey.val = oldKey.val;
                        newKey.valString = oldKey.valString;
                        newKey.valObj = oldKey.valObj;
                        newKey.vect4 = oldKey.vect4;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMRotationEulerTrack) {
                    var oldRotEulerTrack = (AMRotationEulerTrack)oldTrack;
                    var newRotEulerTrack = new RotationEulerTrack();

                    newTrack = newRotEulerTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newRotEulerTrack.axis = (AxisFlags)oldRotEulerTrack.axis;

                    newTrack.keys = new List<Key>();
                    foreach(AMRotationEulerKey oldKey in oldTrack.keys) {
                        var newKey = new RotationEulerKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.rotation = oldKey.rotation;
                        newKey.endFrame = oldKey.endFrame;

                        newTrack.keys.Add(newKey);
                    }
                }
                else if(oldTrack is AMRotationTrack) {
                    newTrack = new RotationTrack();

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);
                                        
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
                    var oldTransTrack = (AMTranslationTrack)oldTrack;
                    var newTransTrack = new TranslationTrack();

                    newTrack = newTransTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, true, isMeta);

                    newTransTrack.pixelPerUnit = oldTransTrack.pixelPerUnit;
                    newTransTrack.pixelSnap = oldTransTrack.pixelSnap;
                                        
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
                else if(oldTrack is AMTriggerTrack) { //convert TriggerTrack to EventTrack with TriggerSignal
                    var newTriggerTrack = new EventTrack();

                    newTrack = newTriggerTrack;

                    ConvertTrackCommonFields(oldTrack, newTrack, false, isMeta);

                    //grab/create signal for this trigger
                    string signalPath = GetTriggerSignalPath(assetPath);

                    TriggerSignal triggerSignal;
                    if(!mTriggerSignalLookup.TryGetValue(signalPath, out triggerSignal)) {
                        //try to load it if it exists
                        triggerSignal = AssetDatabase.LoadAssetAtPath<TriggerSignal>(signalPath);
                        if(!triggerSignal) {
                            AddMessage("  - Creating Trigger Signal: " + signalPath);

                            triggerSignal = ScriptableObject.CreateInstance<TriggerSignal>();
                            AssetDatabase.CreateAsset(triggerSignal, signalPath);
                            AssetDatabase.SaveAssets();

                            yield return new WaitForFixedUpdate();
                        }

                        mTriggerSignalLookup.Add(signalPath, triggerSignal);
                    }

                    newTriggerTrack.SetTargetAsObject(triggerSignal);

                    newTrack.keys = new List<Key>();
                    foreach(AMTriggerKey oldKey in oldTrack.keys) {
                        var newKey = new EventKey();

                        ConvertKeyCommonFields(oldKey, newKey);

                        newKey.useSendMessage = false;
                        newKey.methodName = "Invoke";

                        newKey.parameters = new List<EventParameter>(3);
                        newKey.parameters.Add(new EventParameter() { valueType = EventData.ValueType.String, val_string = oldKey.valueString });
                        newKey.parameters.Add(new EventParameter() { valueType = EventData.ValueType.Integer, val_int = oldKey.valueInt });
                        newKey.parameters.Add(new EventParameter() { valueType = EventData.ValueType.Float, val_float = oldKey.valueFloat });

                        newTrack.keys.Add(newKey);
                    }
                }

                newTake.trackValues.Add(newTrack);

                yield return new WaitForFixedUpdate();
            }
        }

        void ConvertEventData(AMEventData oldParam, EventData newParam) {
            newParam.paramName = oldParam.paramName;
            newParam.valueType = (EventData.ValueType)oldParam.valueType;
            newParam.val_int = oldParam.val_int;
            newParam.val_string = oldParam.val_string;
            newParam.val_vect4 = oldParam.val_vect4;
            newParam.val_obj = oldParam.val_obj;
        }

        void ConvertEventParameter(AMEventParameter oldParam, EventParameter newParam) {
            ConvertEventData(oldParam, newParam);

            newParam.lsArray = new List<EventData>(oldParam.lsArray.Count);
            for(int i = 0; i < oldParam.lsArray.Count; i++) {
                EventData newParamItem;

                var oldParamItem = oldParam.lsArray[i];
                if(oldParamItem is AMEventParameter) {
                    newParamItem = new EventParameter();
                    ConvertEventParameter((AMEventParameter)oldParamItem, (EventParameter)newParamItem);
                }
                else {
                    newParamItem = new EventData();
                    ConvertEventData(oldParamItem, newParamItem);
                }

                newParam.lsArray.Add(newParamItem);
            }
        }

        void ConvertTrackCommonFields(AMTrack oldTrack, Track newTrack, bool applyTarget, bool isMeta) {
            newTrack.id = oldTrack.id;
            newTrack.name = oldTrack.name;
            newTrack.foldout = oldTrack.foldout;

            if(applyTarget) {
                if(isMeta)
                    newTrack.SetTargetDirect(null, oldTrack.targetPath);
                else
                    newTrack.SetTargetDirect(oldTrack.GetTarget(null), "");
            }
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

        string GetTriggerSignalPath(string path) {
            int extInd = path.LastIndexOf('.');
            if(extInd == -1) {
                //create in TriggerSignal folder
                if(!AssetDatabase.IsValidFolder("Assets/TriggerSignal"))
                    AssetDatabase.CreateFolder("Assets", "TriggerSignal");

                return "Assets/TriggerSignal/" + path + "TriggerSignal.asset";
            }
            else
                return path.Substring(0, extInd) + "TriggerSignal.asset";
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
 
 
 
 
 
 
 
 
 
 