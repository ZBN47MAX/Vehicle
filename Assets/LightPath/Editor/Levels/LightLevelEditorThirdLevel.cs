using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DelayNoMore.LightPath.Editor
{
    /** @brief 第三关作者配方，沿用关卡编辑器的持久房间、SO和预制体制作流程。 */
    public sealed partial class LightLevelEditor
    {
        /** @brief 本关资产目录，已有内容不会被再次覆盖。 */
        private const string ThirdFolder = "Assets/LightPath/Rooms/Lv3";
        /** @brief 第三关关卡定义路径。 */
        public const string ThirdLevelPath = "Assets/LightPath/Levels/Level3.asset";
        /** @brief 房间标题。 */
        private static readonly string[] ThirdTitles = { "四色检票", "搬运光桥", "侧立的桥面", "换色接力", "窄面的秘密", "双层中转", "六轴合奏" };
        /** @brief 对应谜题提示，集中保存到正式文案表。 */
        private static readonly string[] ThirdHints = {
            "四种颜色依次打开道路，穿过障碍后再换下一种颜色。",
            "移动桥的位置与高度，调整深度改变桥长，再沿Z轴把桥放平。",
            "先取得X轴旋转。桥面太厚，沿X轴侧转，把它压成可以站立的窄影。",
            "当前背景色会让同色桥消失。跨过两段不同颜色的桥，再清除出口障碍。",
            "先取得Y轴旋转。正对光源的窄面很短，沿Y轴转动，把深度展开成桥。",
            "把投影门移到可达位置。它送你到上层，固定门再送往终点房间。",
            "分别沿X、Y、Z轴展开三段桥，移动它们拼接道路，并切换颜色穿过两道屏障。" };
        /** @brief 创建持久第三关内容，供关卡编辑器按钮和批处理调用。 */
        public static void CreateThirdLevelAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止运行。");
            if (AssetDatabase.LoadAssetAtPath<ProjectionLevel>(ThirdLevelPath)) return;
            if (!AssetDatabase.IsValidFolder(ThirdFolder)) AssetDatabase.CreateFolder("Assets/LightPath/Rooms", "Lv3");
            var catalog = Resources.Load<GameTextCatalog>("GameTextCatalog");
            if (!catalog) throw new InvalidOperationException("文案表缺失。");
            var preview = EditorSceneManager.NewPreviewScene();
            var created = new List<string>();
            var entries = catalog.Entries.ToArray();
            try
            {
                var previous = AssetDatabase.LoadAssetAtPath<ProjectionLevel>("Assets/LightPath/Levels/Level2.asset");
                var config = previous && previous.Config ? Instantiate(previous.Config) : CreateInstance<LightGameConfig>();
                SaveThirdAsset(config, "Assets/LightPath/Levels/Level3-Config.asset", created);
                var level = CreateInstance<ProjectionLevel>();
                level.Config = config; level.HasLayout = true;
                level.Rooms = new ProjectionLevel.RoomConnection[6];
                for (int index = 0; index < 7; index++)
                {
                    var definition = CreateInstance<LightRoomDefinition>();
                    definition.Configure(ThirdTitles[index], ThirdHints[index]);
                    var data = new SerializedObject(definition);
                    string titleKey = "Level3.Room" + index + ".Title", hintKey = "Level3.Room" + index + ".Hint";
                    data.FindProperty("_titleKey").stringValue = titleKey;
                    data.FindProperty("_hintKey").stringValue = hintKey;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    AddThirdText(catalog, titleKey, ThirdTitles[index], index, "房间标题");
                    AddThirdText(catalog, hintKey, ThirdHints[index], index, "解谜提示");
                    SaveThirdAsset(definition, ThirdFolder + "/Level3Room" + index + "-Room.asset", created);
                    LightRoom room = CreateBlankRoom("Level3Room" + index, config, definition, preview);
                    DestroyImmediate(room.transform.Find("基础地板").gameObject);
                    if (index < 6) DestroyImmediate(room.transform.Find("出口").gameObject);
                    LightStagePreview.Configure(room.Surface);
                    BuildThirdRoom(room, index, created);
                    string path = ThirdFolder + "/Level3Room" + index + ".prefab";
                    if (AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("已有资产，请检查 " + path);
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(room.gameObject, path);
                    if (!prefab) throw new InvalidOperationException("房间保存失败。");
                    created.Add(path);
                    if (index == 0) level.RoomPrefab = prefab.GetComponent<LightRoom>();
                    else level.Rooms[index - 1] = new ProjectionLevel.RoomConnection { Prefab = prefab.GetComponent<LightRoom>(), Position = new Vector2Int(index, 0) };
                    DestroyImmediate(room.gameObject);
                }
                for (int index = 0; index < 7; index++)
                {
                    if (index > 0 && index != 6) level.SetEdge(index, 0, ProjectionLevel.EdgeBehavior.Move);
                    if (index < 5) level.SetEdge(index, 1, ProjectionLevel.EdgeBehavior.Move);
                    if (index == 1 || index == 2 || index == 3 || index == 4 || index == 6)
                        level.SetEdge(index, 2, ProjectionLevel.EdgeBehavior.Hazard);
                }
                SaveThirdAsset(level, ThirdLevelPath, created);
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                Selection.activeObject = level;
                Debug.Log("第三关7个持久房间已创建，可在关卡编辑器继续调整。");
            }
            catch
            {
                catalog.Entries = entries.ToList(); EditorUtility.SetDirty(catalog);
                for (int i = created.Count - 1; i >= 0; i--) AssetDatabase.DeleteAsset(created[i]);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        /** @brief 将第三关接到当前正式关卡列表末尾并保存随机循环选项，重复调用不重复添加。 */
        public static void ConnectThirdLevelCampaign()
        {
            var scene = EditorSceneManager.OpenScene("Assets/LightPath/Scenes/LightGame.unity");
            var level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>(ThirdLevelPath);
            if (!level) throw new InvalidOperationException("请先创建第三关。");
            string bridgePath = AssetDatabase.GetAssetPath(level.Prefab(1));
            GameObject bridgeRoom = PrefabUtility.LoadPrefabContents(bridgePath);
            try
            {
                var room = bridgeRoom.GetComponent<LightRoom>();
                var bridge = room.Surface.Sources.Single();
                if (bridge.transform.localPosition == new Vector3(-6, 5, 0))
                {
                    LightStagePreview.Configure(room.Surface);
                    bridge.transform.position = Vector3.LerpUnclamped(room.Surface.ProjectionOrigin, new Vector3(-6, 5, 0), 1.05f);
                    PrefabUtility.SaveAsPrefabAsset(bridgeRoom, bridgePath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(bridgeRoom); }
            var game = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LightGame>(true)).Single();
            var data = new SerializedObject(game);
            var levels = data.FindProperty("_levels");
            bool exists = false;
            for (int i = 0; i < levels.arraySize; i++) exists |= levels.GetArrayElementAtIndex(i).objectReferenceValue == level;
            if (!exists)
            {
                int index = -1;
                for (int i = 0; i < levels.arraySize; i++)
                    if (!levels.GetArrayElementAtIndex(i).objectReferenceValue) { index = i; break; }
                if (index < 0)
                {
                    index = levels.arraySize;
                    levels.arraySize = index + 1;
                }
                levels.GetArrayElementAtIndex(index).objectReferenceValue = level;
                data.ApplyModifiedPropertiesWithoutUndo();
                if (data.FindProperty("_levels").GetArrayElementAtIndex(index).objectReferenceValue != level)
                    throw new InvalidOperationException("第三关引用保存失败。");
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            var repair = data.FindProperty("_repairLayout").objectReferenceValue as RepairLayout;
            if (repair) { repair.RandomPhaseOrder = true; EditorUtility.SetDirty(repair); }
            AssetDatabase.SaveAssets();
        }
        /** @brief 保存本次新资产并记录回滚。@param asset 资产 @param path 路径 @param created 新资产列表 */
        private static void SaveThirdAsset(UnityEngine.Object asset, string path, List<string> created)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("已有资产 " + path);
            AssetDatabase.CreateAsset(asset, path); created.Add(path);
        }
        /** @brief 登记集中管理的教学文案。@param catalog 文案表 @param key 标识 @param value 正文 @param room 房间 @param component 用途 */
        private static void AddThirdText(GameTextCatalog catalog, string key, string value, int room, string component)
        {
            if (catalog.Entries.Any(x => x.Key == key)) return;
            catalog.Entries.Add(new GameTextCatalog.Entry { Key = key, Value = value, Screen = "2D世界", NormalPlay = true,
                Component = "LightRoomDefinition / " + component, Title = "第三关" + ThirdTitles[room] + component,
                Purpose = "进入第三关房间" + room + "时显示", Location = "游戏HUD / " + component });
        }
        /** @brief 构建房间的作者内容，全部保存为可编辑对象。@param room 房间 @param index 谜题编号 @param created 新资产 */
        private static void BuildThirdRoom(LightRoom room, int index, List<string> created)
        {
            var root = room.gameObject;
            bool pit = index == 1 || index == 2 || index == 3 || index == 4 || index == 6;
            if (pit)
            {
                ThirdPlatform(root, "左岸", new Vector2(-6, -.5f), new Vector2(6, 1), -1);
                ThirdPlatform(root, "右岸", new Vector2(6, -.5f), new Vector2(6, 1), -1);
            }
            else ThirdPlatform(root, "地面", new Vector2(0, -.5f), new Vector2(18, 1), -1);
            if (index == 0)
                for (int color = 0; color < 4; color++) ThirdPlatform(root, "四色屏障" + color, new Vector2(-5 + color * 3.3f, 4), new Vector2(.5f, 8), color);
            if (index == 1)
                ThirdSource(room, "XYZ_Z_Bridge", new Vector2(-6, 5), new Vector3(6.8f, .35f, .2f), 1.05f, new Vector3(0, 0, 90), -1, new Vector3Int(0, 0, 1), new Vector3(15, 15, 30), created);
            if (index == 2 || index == 4)
            {
                if (index == 4)
                {
                    root.transform.Find("左岸").localScale = new Vector3(3, 1, .2f);
                    root.transform.Find("左岸").localPosition = new Vector3(-7.5f, -.5f, 0);
                    root.transform.Find("右岸").localScale = new Vector3(3, 1, .2f);
                    root.transform.Find("右岸").localPosition = new Vector3(7.5f, -.5f, 0);
                }
                ThirdKey(root, index == 2 ? ProjectionAbility.RotateX : ProjectionAbility.RotateY);
                bool x = index == 2;
                ThirdSource(room, x ? "X_Plate" : "Y_Beam", new Vector2(0, .65f), x ? new Vector3(7.2f, 4, .25f) : new Vector3(.25f, .4f, 7.2f), .5f,
                    Vector3.zero, -1, x ? new Vector3Int(1, 0, 0) : new Vector3Int(0, 1, 0), Vector3.zero, created);
            }
            if (index == 3)
            {
                ThirdSource(room, "蓝色桥", new Vector2(-1.7f, .6f), new Vector3(3.8f, .35f, .15f), .5f, Vector3.zero, 0, Vector3Int.zero, Vector3.zero, created);
                ThirdSource(room, "金色桥", new Vector2(1.7f, .6f), new Vector3(3.8f, .35f, .15f), .5f, Vector3.zero, 1, Vector3Int.zero, Vector3.zero, created);
                ThirdPlatform(root, "金色入口屏障", new Vector2(-3.2f, 4), new Vector2(.3f, 8), 1);
                ThirdPlatform(root, "蓝色出口屏障", new Vector2(3.2f, 4), new Vector2(.3f, 8), 0);
                ThirdPlatform(root, "中继台", new Vector2(0, .25f), new Vector2(1.5f, .5f), -1);
            }
            if (index == 5)
            {
                ThirdPlatform(root, "上层中转台", new Vector2(6, 3.5f), new Vector2(4, .5f), -1);
                var gate = ThirdSource(room, "可移动投影门", new Vector2(-2, 4), new Vector3(1, 1.8f, .15f), .6f, Vector3.zero, 2, Vector3Int.zero, new Vector3(6, 6, 4), created, true);
                var link = gate.gameObject.AddComponent<RoomPortalLink>(); link.TargetRoom = 5; link.UseArrivalPosition = true; link.ArrivalPosition = new Vector2(6, 4.6f);
                GameObject fixedGate = ThirdPlatform(root, "固定2D出口门", new Vector2(6, 4.6f), new Vector2(.8f, 1.5f), 3);
                fixedGate.GetComponent<BoxCollider2D>().isTrigger = true;
                var next = fixedGate.AddComponent<RoomPortalLink>(); next.TargetRoom = 6; next.UseArrivalPosition = true; next.ArrivalPosition = new Vector2(-7, 1);
            }
            if (index == 6)
            {
                // 三段桥拼接大缺口，X/Y/Z轴分别锁定，确保三种旋转各有实际用途。
                root.transform.Find("左岸").localScale = new Vector3(3, 1, .2f);
                root.transform.Find("左岸").localPosition = new Vector3(-7.5f, -.5f, 0);
                root.transform.Find("右岸").localScale = new Vector3(3, 1, .2f);
                root.transform.Find("右岸").localPosition = new Vector3(7.5f, -.5f, 0);
                ThirdSource(room, "终局X桥", new Vector2(-4, .65f), new Vector3(4.4f, 3, .25f), .5f, Vector3.zero, 0, new Vector3Int(1,0,0), new Vector3(1,1,0), created);
                ThirdSource(room, "终局Y桥", new Vector2(0, .65f), new Vector3(.25f, .4f, 4.4f), .5f, Vector3.zero, 1, new Vector3Int(0,1,0), new Vector3(1,1,0), created);
                ThirdSource(room, "终局Z桥", new Vector2(4, .65f), new Vector3(4.4f, .35f, .2f), .5f, new Vector3(0,0,90), -1, new Vector3Int(0,0,1), new Vector3(1,1,2), created);
                ThirdPlatform(root, "绿屏障", new Vector2(-5.7f, 4), new Vector2(.3f, 8), 2);
                ThirdPlatform(root, "玫红屏障", new Vector2(5.7f, 4), new Vector2(.3f, 8), 3);
            }
            GameObject checkpoint = ThirdPlatform(root, "存档点", new Vector2(-7, .55f), new Vector2(.5f, .5f), LightGameConfig.RedColorIndex);
            checkpoint.GetComponent<BoxCollider2D>().isTrigger = true;
            checkpoint.AddComponent<RoomCheckpoint>();
            checkpoint.GetComponent<ColorPlatform>().Configure(LightGameConfig.RedColorIndex);
            checkpoint.transform.localRotation = Quaternion.Euler(0, 0, 45);
            checkpoint.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/LightPath/Prefabs/Markers/Checkpoint.mat");
            room.Configure(room.Definition, room.Surface, root.GetComponentsInChildren<ColorPlatform>(true), root.transform.Find("出生点"),
                index == 6 ? root.transform.Find("出口") : null, root.transform.Find("幕布").GetComponent<Renderer>());
            var sources = new SerializedObject(room.Surface).FindProperty("_sources");
            ProjectionSource[] all = root.GetComponentsInChildren<ProjectionSource>(true);
            sources.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++) sources.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            sources.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
        /** @brief 创建持久固定二维节点。@param root 房间 @param name 名称 @param position 中心 @param size 尺寸 @param color 色号 @return 节点 */
        private static GameObject ThirdPlatform(GameObject root, string name, Vector2 position, Vector2 size, int color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/LightPath/Materials/Environment/Platform_0.mat");
            GameObject platform = CubeChild(root, name, position, new Vector3(size.x, size.y, .2f), material);
            platform.layer = 9; platform.AddComponent<BoxCollider2D>(); platform.AddComponent<ColorPlatform>().Configure(color);
            return platform;
        }
        /** @brief 创建二维能力钥匙，实际接触后才解锁。@param root 房间 @param ability 新能力 */
        private static void ThirdKey(GameObject root, ProjectionAbility ability)
        {
            GameObject key = ThirdPlatform(root, ability.ToString() + "钥匙", new Vector2(-6, 1), Vector2.one * .6f, -1);
            key.GetComponent<BoxCollider2D>().isTrigger = true;
            key.AddComponent<ProjectionAbilityKey>().Configure(ability);
        }
        /** @brief 按舞台真实光源逆向布设模型，保存作者初始姿态。@param room 房间 @param name 名称 @param projected 初始投影中心 @param size 参考投影尺寸 @param depth 光源到幕布比例 @param rotation 初始旋转 @param color 色号 @param axes 允许旋转轴 @param travel 移动范围 @param created 新资产 @param portal 是否门 @return 投影源 */
        private static ProjectionSource ThirdSource(LightRoom room, string name, Vector2 projected, Vector3 size, float depth, Vector3 rotation, int color, Vector3Int axes, Vector3 travel, List<string> created, bool portal = false)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/LightPath/Materials/Environment/Platform_0.mat");
            Vector3 position = Vector3.LerpUnclamped(room.Surface.ProjectionOrigin, new Vector3(projected.x, projected.y, 0), depth);
            GameObject model = CubeChild(room.gameObject, name, position, size * .5f, material);
            model.transform.localRotation = Quaternion.Euler(rotation); model.AddComponent<BoxCollider>();
            var definition = CreateInstance<ProjectionDefinition>();
            definition.Configure(color >= 0 || portal, portal, color, true, Color.white);
            var data = new SerializedObject(definition);
            data.FindProperty("_travel").vector3Value = travel;
            data.ApplyModifiedPropertiesWithoutUndo();
            SaveThirdAsset(definition, ThirdFolder + "/" + room.name + "-" + name + "-Projection.asset", created);
            var source = model.AddComponent<ProjectionSource>(); source.Configure(definition);
            return source;
        }
    }
}
