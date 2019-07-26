using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Egret3DExportTools
{
    public class ExportToolsWindow : EditorWindow
    {
        private enum ExportType
        {
            NONE, PREFAB, SCENE, TEXTURE
        }
        public const string VERSION = "v1.3.1";//版本号
        private const float WIN_WIDTH = 500.0f;
        private const float WIN_HEIGHT = 400.0f;
        private const float SMALL_SPACE = 10.0f;
        private const float SPACE = 20.0f;

        private static GUIStyle _showStyle = new GUIStyle();//Label的样式

        /**
         * 初始化插件窗口
         */
        [MenuItem("Egret3DExportTools/OpenWindow")]
        public static void ShowWindow()
        {
            var window = EditorWindow.GetWindow<ExportToolsWindow>(true, "Egret3D Export Tools" + VERSION);
            window.minSize = new Vector2(WIN_WIDTH, WIN_HEIGHT);
            window.maxSize = window.minSize;
            window.Show();
        }
        /**
         * 导出预制体
         */
        public static void ExportPrefabs()
        {
            var selectionObjs = Selection.gameObjects;
            foreach (var selectionObj in selectionObjs)
            {
                //防止egret 序列化报错
                var saveParent = selectionObj.transform.parent;
                selectionObj.transform.parent = null;
                PathHelper.SetOutPutPath(ExportConfig.instance.exportPath, selectionObj.name);
                ExportPrefabTools.ExportPrefab(selectionObj, PathHelper.OutPath);
                selectionObj.transform.parent = saveParent;
            }
        }
        /**
         * 导出场景
         */
        public static void ExportCurScene()
        {
            ExportExtendTools.CleanupMissingScripts();
            //获取场景中的根gameObject
            List<GameObject> roots = new List<GameObject>();
            GameObject[] allObjs = GameObject.FindObjectsOfType<GameObject>();
            for (int i = 0; i < allObjs.Length; i++)
            {
                var tempObj = allObjs[i];
                while (tempObj.transform.parent != null)
                {
                    tempObj = tempObj.transform.parent.gameObject;
                }
                if (!roots.Contains(tempObj))
                {
                    roots.Add(tempObj);
                }
            }
            ExportSceneTools.ExportScene(roots, PathHelper.OutPath);
        }
        /**
         * 导出贴图
         */
        public static void ExportTextures()
        {
            var selectionObjs = Selection.GetFiltered<Texture>(SelectionMode.TopLevel);
            Debug.Log(selectionObjs.Length);
            Shader shader = Shader.Find("Unlit/Texture");
            foreach (var selectionObj in selectionObjs)
            {
                Texture select = selectionObj;
                string path = AssetDatabase.GetAssetPath(select.GetInstanceID());

                string assetsPath = path.Substring(0, path.LastIndexOf('/'));
                string savePath = Path.Combine(ExportConfig.instance.exportPath, assetsPath);
                UnityEngine.Debug.Log(savePath);
                SaveRenderTextureToPNG(select, shader, savePath, assetsPath, select.name);
            }
        }
        private static void WriteJson(string savePath, string assetsPath, RenderTexture select, string name)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\n");
            sb.Append(string.Format("\"name\":\"{0}/{1}.png\",", assetsPath, name));
            sb.Append("\n");
            sb.Append(string.Format("\"filterMode\":\"{0}\",", select.filterMode));
            sb.Append("\n");
            sb.Append(string.Format("\"wrap\":\"{0}\",", select.wrapMode));
            sb.Append("\n");
            sb.Append(string.Format("\"mipmap\":{0},", select.useMipMap.ToString().ToLower()));
            sb.Append("\n");
            sb.Append(string.Format("\"version\":{0}", 2));
            sb.Append("\n");
            sb.Append("}");
            string json = Regex.Replace(sb.ToString(), "(\"(?:[^\"\\\\]|\\\\.)*\")|\\s+", "$1");
            string path = string.Format("{0}/{1}.image.json", savePath, name);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            using (FileStream fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                BinaryWriter writer = new BinaryWriter(fs);
                writer.Write(bytes);
            }
        }

        public static bool SaveRenderTextureToPNG(Texture inputTex, Shader outputShader, string contents, string assetsPath, string pngName)
        {
            RenderTexture temp = RenderTexture.GetTemporary(inputTex.width, inputTex.height, 0, RenderTextureFormat.ARGB32);
            Material mat = new Material(outputShader);

            Graphics.Blit(inputTex, temp, mat);
            bool ret = SaveRenderTextureToPNG(temp, contents, pngName);

            WriteJson(contents, assetsPath, temp, pngName);

            RenderTexture.ReleaseTemporary(temp);
            return ret;

        }

        //将RenderTexture保存成一张png图片
        public static bool SaveRenderTextureToPNG(RenderTexture rt, string contents, string pngName)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D png = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
            png.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            byte[] bytes = png.EncodeToPNG();
            if (!Directory.Exists(contents))
                Directory.CreateDirectory(contents);
            string path = contents + "/" + pngName + ".png";
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            using (FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                BinaryWriter writer = new BinaryWriter(fs);
                writer.Write(bytes);
            }
            Texture2D.DestroyImmediate(png);
            png = null;
            RenderTexture.active = prev;
            return true;

        }

        //
        private Vector2 _scrollPosition = new Vector2(0, 0);
        private string _info = "就绪";
        private bool _isBuzy = false;
        private int _frameCount = 0;
        private ExportType _curExportType;

        /*导出类型的单选框状态*/
        private bool _resourceToolOpen = false;//资源
        private bool _sceneToolOpen = false;//场景
        private bool _textureToolOpen = false;

        private bool _lightSetting = true;
        private bool _meshSetting = false;
        private SerializedObject _serializeObject;
        private SerializedProperty _meshIgnoresProperty;

        void OnEnable()
        {
            _showStyle.fontSize = 15;
            _showStyle.normal.textColor = new Color(1, 0, 0, 1);

            //
            _serializeObject = new SerializedObject(ExportToolsSetting.instance);
            _meshIgnoresProperty = _serializeObject.FindProperty("meshIgnores");
            //

            //加载配置文件
            ExportConfig.Reload(PathHelper.ConfigPath, PathHelper.SaveRootDirectory);
            //初始化一些全局的方法
            SerializeObject.Initialize();
            GLTFInitialize.Initialize();
        }

        /**
         * 绘制窗口
         */
        void OnGUI()
        {
            var setting = ExportToolsSetting.instance;
            this._scrollPosition = GUILayout.BeginScrollView(this._scrollPosition, GUILayout.Width(WIN_WIDTH), GUILayout.Height(400));
            GUILayout.Space(SMALL_SPACE);
            //------------------------目录选择------------------------
            {
                GUILayout.Label("当前导出路径");
                GUILayout.BeginHorizontal();
                GUILayout.TextField(ExportConfig.instance.exportPath);
                if (GUILayout.Button("选择目录", GUILayout.Width(100)))
                {
                    ExportConfig.instance.exportPath = EditorUtility.OpenFolderPanel("当前要导出的路径", Application.dataPath, "");
                    ExportConfig.instance.Save(PathHelper.ConfigPath);
                    AssetDatabase.Refresh();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(SPACE);
            //------------------------辅助选项------------------------
            {
                GUILayout.BeginHorizontal();
                setting.debugLog = GUILayout.Toggle(setting.debugLog, new GUIContent("输出日志", "勾选后，方便查看输出信息"));
                setting.prefabResetPos = GUILayout.Toggle(setting.prefabResetPos, new GUIContent("坐标归零", "勾选后，将导出的预制体坐标归零"));
                setting.exportOriginalImage = GUILayout.Toggle(setting.exportOriginalImage, new GUIContent("导出原始图片", "勾选后，jpg和png会直接使用原始图片导出"));
                // ExportToolsSetting.unityNormalTexture = GUILayout.Toggle(ExportToolsSetting.unityNormalTexture, new GUIContent("使用Unity法线贴图", "勾选后，时使用Unity转换后的法线贴图导出"));

                GUILayout.EndHorizontal();
            }
            GUILayout.Space(SPACE);
            this._lightSetting = EditorGUILayout.Foldout(this._lightSetting, "光照设置");
            if (this._lightSetting)
            {
                GUILayout.BeginVertical();
                setting.lightType = (ExportLightType)EditorGUILayout.EnumPopup(setting.lightType, GUILayout.MaxWidth(100));
                GUILayout.EndVertical();
            }
            GUILayout.Space(SMALL_SPACE);
            this._meshSetting = EditorGUILayout.Foldout(this._meshSetting, "网格设置");
            if (this._meshSetting)
            {
                GUILayout.BeginHorizontal();
                setting.enableNormals = GUILayout.Toggle(setting.enableNormals, new GUIContent("Normals", "取消后，不导出Normals"));
                setting.enableColors = GUILayout.Toggle(setting.enableColors, new GUIContent("Colors", "取消后，不导出Colors"));
                setting.enableBones = GUILayout.Toggle(setting.enableBones, new GUIContent("Bones", "取消后，不导出Bones"));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                this._serializeObject.Update();
                if (this._meshIgnoresProperty != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(this._meshIgnoresProperty, new GUIContent("忽略对象:", "在忽略列表中的对象网格属性全部导出"), true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _serializeObject.ApplyModifiedProperties();
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(SPACE);
            //------------------------主功能------------------------
            {
                //资源导出
                _resourceToolOpen = GUILayout.Toggle(_resourceToolOpen, "--------资源导出工具--------");
                if (_resourceToolOpen)
                {
                    GUILayout.Space(SPACE);
                    GUILayout.BeginHorizontal();
                    if (Selection.activeGameObject)
                    {
                        if (GUILayout.Button("导出当前选中对象"))
                        {
                            _frameCount = 0;
                            _isBuzy = true;
                            _info = "导出中...";
                            _curExportType = ExportType.PREFAB;
                        }
                    }
                    else
                    {
                        GUILayout.Label("请选中场景中要导出的对象", _showStyle);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(SPACE);
                }
                //场景导出
                _sceneToolOpen = GUILayout.Toggle(_sceneToolOpen, "--------场景导出工具--------");
                if (_sceneToolOpen)
                {
                    GUILayout.Space(SPACE);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("导出当前场景"))
                    {
                        PathHelper.SetOutPutPath(ExportConfig.instance.exportPath, PathHelper.CurSceneName);

                        _frameCount = 0;
                        _info = "导出中...";
                        _isBuzy = true;
                        _curExportType = ExportType.SCENE;
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(SPACE);
                }
                // 贴图导出  Texture
                _textureToolOpen = GUILayout.Toggle(_textureToolOpen, "--------贴图导出工具--------");
                if (_textureToolOpen)
                {
                    GUILayout.Space(SPACE);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("导出当前选中的贴图"))
                    {
                        _frameCount = 0;
                        _isBuzy = true;
                        _info = "导出中...";
                        _curExportType = ExportType.TEXTURE;
                        //ExportTextures();
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(SPACE);
                }
            }

            GUILayout.EndScrollView();
            GUI.Label(new Rect(0, WIN_HEIGHT - 15, WIN_WIDTH, 15), "状态：" + _info);
        }

        void OnInspectorUpdate()
        {
            this.Repaint();
        }

        void Update()
        {
            if (!_isBuzy)
            {
                return;
            }

            _frameCount++;
            //第二帧再处理，保证能播起来
            if (_frameCount == 2)
            {
                switch (_curExportType)
                {
                    case ExportType.PREFAB:
                        ExportPrefabs();
                        break;
                    case ExportType.SCENE:
                        ExportCurScene();
                        break;
                    case ExportType.TEXTURE:
                        ExportTextures();
                        break;
                    default:
                        break;
                }

                //导出完毕后，恢复初始值
                _frameCount = 0;
                _isBuzy = false;
                _curExportType = ExportType.NONE;
                _info = "就绪";
            }
        }


        // public static void BakeSkinnedMeshRenderer()
        // {
        //     var selection = Selection.activeGameObject;
        //     if (selection == null)
        //     {
        //         return;
        //     }
        //     var skinned = selection.GetComponentInChildren<SkinnedMeshRenderer>();
        //     if (skinned == null || skinned.sharedMesh == null)
        //     {
        //         return;
        //     }
        //     //
        //     var mesh = new Mesh();
        //     skinned.BakeMesh(mesh);
        //     var url = UnityEditor.AssetDatabase.GetAssetPath(skinned.sharedMesh);
        //     string name = selection.name + ".asset";
        //     url = url.Substring(0, url.LastIndexOf("/") + 1) + name;
        //     AssetDatabase.CreateAsset(mesh, url);
        // }
    }
}
