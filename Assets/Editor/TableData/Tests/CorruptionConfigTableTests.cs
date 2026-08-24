using Corruption;
using NUnit.Framework;
using UnityEditor;

namespace TableDataEditor.Tests
{
    public sealed class CorruptionConfigTableTests
    {
        [Test]
        public void LiveCsv_DefaultConfigAndGeneratedAssetsMatch()
        {
            TableDataValidationResult result = TableDataValidator.Validate();
            Assert.Zero(result.ErrorCount, result.Summary);
            Assert.AreEqual(1, result.Snapshot.CorruptionConfigs.Count);
            CorruptionConfigRow row = result.Snapshot.CorruptionConfigs[0];
            Assert.AreEqual("default", row.Id);
            Assert.AreEqual(300, row.MaxCorruption);
            Assert.AreEqual(50, row.WarningThresholdPercent);
            Assert.AreEqual(80, row.DangerThresholdPercent);
            Assert.AreEqual(2, row.WarningStaminaCostMultiplier);
            Assert.AreEqual(3, row.DangerStaminaCostMultiplier);

            CorruptionConfigDefinition definition = AssetDatabase.LoadAssetAtPath<CorruptionConfigDefinition>(
                TableDataPaths.CorruptionConfigAssetPath("default"));
            CorruptionConfigCatalog catalog = AssetDatabase.LoadAssetAtPath<CorruptionConfigCatalog>(
                TableDataPaths.CorruptionConfigCatalogAssetPath);
            Assert.IsNotNull(definition);
            Assert.IsNotNull(catalog);
            Assert.AreSame(definition, catalog.Find("default"));
        }

        [Test]
        public void Scope_CorruptionConfigOnlySelectsItsOwnOutputFolder()
        {
            CollectionAssert.AreEqual(new[] { TableDataPaths.CorruptionConfigOutputFolder },
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.CorruptionConfigTable));
            Assert.IsTrue(TableDataRebuildScopes.IncludesCorruptionConfigTable(TableDataRebuildScope.All));
            Assert.IsTrue(TableDataRebuildScopes.IncludesCorruptionConfigTable(TableDataRebuildScope.CorruptionConfigTable));
            Assert.IsFalse(TableDataRebuildScopes.IncludesLegacyDomains(TableDataRebuildScope.CorruptionConfigTable));
        }
    }
}
