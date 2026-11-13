using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DelayNoMore.LightPath.Editor;

namespace DelayNoMore.LightPath.Tests
{
    /** @brief 第三关已有持久资产、连接、新能力及桥梁解法回归。 */
    public sealed class ThirdLevelTests
    {
        /** @brief 七房间连接完整，只有终点有出口，X/Y钥匙分阶段出现。 */
        [Test] public void AuthoredThirdLevel_HasProgressionAndPersistentRooms()
        {
            var level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>(LightLevelEditor.ThirdLevelPath);
            Assert.That(level, Is.Not.Null); Assert.That(level.RoomCount, Is.EqualTo(7));
            for (int index = 0; index < 7; index++)
            {
                LightRoom room = level.Prefab(index);
                Assert.That(room, Is.Not.Null);
                Assert.That(room.GetComponentsInChildren<RoomCheckpoint>(true).Length, Is.EqualTo(1));
                Assert.That(room.GetComponentsInChildren<LevelExit>(true).Length, Is.EqualTo(index == 6 ? 1 : 0));
                if (index < 5) Assert.That(level.Edge(index, 1), Is.EqualTo(ProjectionLevel.EdgeBehavior.Move));
                var keys = room.GetComponentsInChildren<ProjectionAbilityKey>(true);
                if (index == 2 || index == 4)
                    Assert.That(keys.Single().Ability, Is.EqualTo(index == 2 ? ProjectionAbility.RotateX : ProjectionAbility.RotateY));
                else Assert.That(keys, Is.Empty);
            }
            Assert.That(level.Prefab(5).GetComponentsInChildren<RoomPortalLink>(true).Length, Is.EqualTo(2));
            var carry = level.Prefab(1).Surface.Sources.Single();
            Assert.That(carry.transform.localPosition.z, Is.GreaterThan(0), "必须先沿Z移入光源与幕布之间");
            Assert.That(carry.transform.localPosition.x + carry.transform.localScale.magnitude * .5f, Is.LessThan(-3), "必须沿X把模型移入缺口范围");
            Assert.That(carry.transform.localPosition.y - carry.transform.localScale.magnitude * .5f, Is.GreaterThan(3), "必须沿Y降低桥面");
            Assert.That(level.Prefab(4).transform.Find("左岸").localPosition.x, Is.EqualTo(-7.5f));
            Assert.That(level.Prefab(4).transform.Find("右岸").localPosition.x, Is.EqualTo(7.5f));
        }
        /** @brief 教学桥实际旋转后形成可站立投影，未保存的编辑副本用于验证。@param index 房间编号 @param axis 解法旋转轴 */
        [TestCase(2, 0), TestCase(4, 1)]
        public void TeachingBridge_RotationCreatesWalkableShadow(int index, int axis)
        {
            var level = AssetDatabase.LoadAssetAtPath<ProjectionLevel>(LightLevelEditor.ThirdLevelPath);
            GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(level.Prefab(index)));
            try
            {
                var room = root.GetComponent<LightRoom>();
                typeof(LightLevelEditor).Assembly.GetType("DelayNoMore.LightPath.Editor.LightStagePreview")
                    .GetMethod("Configure").Invoke(null, new object[] { room.Surface });
                var source = room.Surface.Sources.Single();
                Assert.That(source.Definition.AllowsRotation(axis), Is.True);
                for (int other = 0; other < 3; other++)
                    if (other != axis) Assert.That(source.Definition.AllowsRotation(other), Is.False);
                source.Adjust(Vector3.zero, 90, axis);
                var geometry = new ProjectionPreviewGeometry(room.Surface.Sources);
                Vector3 origin = room.Surface.ProjectionOrigin;
                for (float x = -2.8f; x < 3; x += .7f)
                {
                    Vector3 point = new Vector3(x, .65f, 0);
                    var ray = new Ray(origin, (point - origin).normalized);
                    Assert.That(geometry.Intersects(source, ray, Vector3.Distance(point, origin)), Is.True, "桥面 " + x + " 光源 " + origin + " 中心 " + source.transform.position + " 投影中心 " + room.Surface.ProjectPoint(source.transform.position) + " 旋转 " + source.transform.eulerAngles + " 尺寸 " + source.transform.lossyScale);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
