using GuidGraph.Analyzer.Code;
using GuidGraph.Analyzer.Scanning;
using GuidGraph.Analyzer.Storage;
using GuidGraph.Graph;

namespace GuidGraph.Tests;

/// <summary>
/// Schema v3 code_symbols: declared types and XML-documented members of .cs files, as
/// keyword-search clues. Synthetic sources only (no fixture needed).
/// </summary>
public class CodeSymbolsTests
{
    private const string EnemySource =
        """
        using UnityEngine;

        namespace Game.Enemies
        {
            /// <summary>
            /// 敵キャラクターの基底クラス。<see cref="Spawner"/> から生成される。
            /// </summary>
            /// <remarks>HP が 0 になると <paramref name="x"/> を無視して消える。</remarks>
            [DisallowMultipleComponent]
            public class Enemy : MonoBehaviour
            {
                /// <summary>Maximum <c>hit points</c> &amp; armor.</summary>
                [SerializeField] private int maxHp, armor;

                /// <summary>Applies damage; see <see langword="null"/> handling.</summary>
                public void TakeDamage(int amount) { }

                public void Undocumented() { }

                /** <summary>Nested state.</summary> */
                public enum State { Idle, /// <summary>Chasing the player.</summary>
                    Chase }
            }

            /// plain line without tags
            public interface IDamageable { }

            public delegate void Died(Enemy enemy);
        }
        """;

    [Fact]
    public void ParserRecordsTypesAlwaysAndMembersOnlyWhenDocumented()
    {
        var symbols = CSharpSourceParser.Parse(EnemySource).Symbols;

        Assert.Equal(
            [
                ("Game.Enemies.Enemy", "class",
                    "敵キャラクターの基底クラス。 Spawner から生成される。 HP が 0 になると x を無視して消える。"),
                ("Game.Enemies.Enemy.maxHp, armor", "field", "Maximum hit points & armor."),
                ("Game.Enemies.Enemy.TakeDamage", "method", "Applies damage; see null handling."),
                ("Game.Enemies.Enemy.State", "enum", "Nested state."),
                ("Game.Enemies.Enemy.State.Chase", "enum_member", "Chasing the player."),
                ("Game.Enemies.IDamageable", "interface", "plain line without tags"),
                ("Game.Enemies.Died", "delegate", null),
            ],
            symbols.Select(s => (s.Symbol, s.Kind, s.Doc)));
    }

    [Fact]
    public void BuildWritesSymbolsAndUpdateMatchesFullRebuild()
    {
        using var project = new TempUnityProject();
        WriteScript(project, "Assets/Scripts/Enemies/Enemy.cs", "11111111111111111111111111111111", EnemySource);
        WriteScript(project, "Assets/Scripts/Boss.cs", "22222222222222222222222222222222",
            "namespace Game { public class Boss : Game.Enemies.Enemy { } }");

        using var db = GraphDatabase.CreateInMemory();
        GraphBuilder.Build(project.Root, UnityProjectScanner.Scan(project.Root), db);
        Assert.Contains(
            "code_symbol|11111111111111111111111111111111|Game.Enemies.Enemy.TakeDamage|method|Applies damage; see null handling.",
            GraphUpdaterTests.Dump(db));

        // Editing only a doc comment must refresh the symbols (the code layer reruns).
        File.WriteAllText(Path.Combine(project.Root, "Assets/Scripts/Boss.cs"),
            "namespace Game { /// <summary>ラスボス</summary>\npublic class Boss : Game.Enemies.Enemy { } }");
        var update = GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);
        Assert.True(update.CodeLayerReanalyzed);
        Assert.Contains("code_symbol|22222222222222222222222222222222|Game.Boss|class|ラスボス", GraphUpdaterTests.Dump(db));
        GraphUpdaterTests.AssertMatchesFullRebuild(project, db);

        // Deleting a script drops its symbols too.
        project.DeleteAsset("Assets/Scripts/Boss.cs");
        GraphUpdater.Update(project.Root, UnityProjectScanner.Scan(project.Root), db);
        Assert.DoesNotContain(GraphUpdaterTests.Dump(db), row => row.Contains("Game.Boss"));
        GraphUpdaterTests.AssertMatchesFullRebuild(project, db);
    }

    private static void WriteScript(TempUnityProject project, string relativePath, string guid, string text)
    {
        var path = Path.Combine(project.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.WriteAllText(path + ".meta", $"fileFormatVersion: 2\nguid: {guid}\n");
    }
}
