using System.IO;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class AuditContractAndPagingTests
    {
        private const string TempRoot = "Assets/Temp/AuditContractAndPagingTests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);
        }

        [TearDown]
        public void TearDown()
        {
#if UNITY_2022_2_OR_NEWER
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
#else
            foreach (var go in Object.FindObjectsOfType<GameObject>())
#endif
            {
                if (go.name.StartsWith("AuditPaging_"))
                    Object.DestroyImmediate(go);
            }

            if (AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.DeleteAsset(TempRoot);
            CleanupEmptyParentFolders(TempRoot);
        }

        [Test]
        public void FindGameObjects_PagesByCursorAndPageSize()
        {
            new GameObject("AuditPaging_Target");
            new GameObject("AuditPaging_Target");

            var first = ToJObject(FindGameObjects.HandleCommand(new JObject
            {
                ["searchMethod"] = "by_name",
                ["searchTerm"] = "AuditPaging_Target",
                ["pageSize"] = 1,
                ["cursor"] = 0
            }));
            var second = ToJObject(FindGameObjects.HandleCommand(new JObject
            {
                ["searchMethod"] = "by_name",
                ["searchTerm"] = "AuditPaging_Target",
                ["pageSize"] = 1,
                ["cursor"] = 1
            }));

            Assert.IsTrue(first.Value<bool>("success"));
            Assert.IsTrue(second.Value<bool>("success"));
            var firstData = (JObject)first["data"];
            var secondData = (JObject)second["data"];
            Assert.AreEqual(2, firstData.Value<int>("totalCount"));
            Assert.AreEqual(1, firstData.Value<int>("nextCursor"));
            Assert.IsTrue(firstData.Value<bool>("hasMore"));
            Assert.AreNotEqual(
                firstData["instanceIDs"]![0]!.Value<int>(),
                secondData["instanceIDs"]![0]!.Value<int>());
        }

        [Test]
        public void FindInFile_FindReferences_UsesBoundaryAndPagingCap()
        {
            var result = ToJObject(FindInFile.HandleCommand(new JObject
            {
                ["action"] = "find_references",
                ["symbolName"] = nameof(AuditContractAndPagingTests),
                ["scope"] = "Assets/Tests/EditMode/Tools",
                ["pageSize"] = 1,
                ["cursor"] = 0,
                ["maxResults"] = 1
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var data = (JObject)result["data"];
            Assert.AreEqual(1, data.Value<int>("totalCount"));
            Assert.AreEqual(1, data.Value<int>("pageSize"));
            Assert.AreEqual(1, ((JArray)data["references"]).Count);
            Assert.That(data["references"]![0]!["content"]!.ToString(), Does.Contain(nameof(AuditContractAndPagingTests)));
        }

        [Test]
        public void ManageComponents_ListAll_PagesInactiveTargets()
        {
            var go = new GameObject("AuditPaging_InactiveComponents");
            go.AddComponent<Rigidbody>();
            go.SetActive(false);

            var result = ToJObject(ManageComponents.HandleCommand(new JObject
            {
                ["action"] = "list_all",
                ["gameObjectPath"] = "AuditPaging_InactiveComponents",
                ["includeInactive"] = true,
                ["pageSize"] = 1,
                ["cursor"] = 0
            }));

            Assert.IsTrue(result.Value<bool>("success"));
            var data = (JObject)result["data"];
            Assert.AreEqual(1, ((JArray)data["components"]).Count);
            Assert.GreaterOrEqual(data.Value<int>("totalCount"), 2);
            Assert.IsTrue(data.Value<bool>("hasMore"));
        }
    }
}
