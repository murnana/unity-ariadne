using UnityAriadne.Analyzer.Code;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Tests;

public class CSharpSourceParserTests
{
    [Fact]
    public void DeclaredTypesCarryNamespaceAndNesting()
    {
        var model = CSharpSourceParser.Parse(
            """
            namespace A.B
            {
                public class Foo<T>
                {
                    private enum Inner { X }
                }

                interface IBar { }
            }

            struct Global { }
            delegate void Callback();
            """);

        Assert.Equal(
            [
                new DeclaredType("Foo", "A.B.Foo", "A.B", IsNested: false),
                new DeclaredType("Inner", "A.B.Foo.Inner", "A.B", IsNested: true),
                new DeclaredType("IBar", "A.B.IBar", "A.B", IsNested: false),
                new DeclaredType("Global", "Global", "", IsNested: false),
                new DeclaredType("Callback", "Callback", "", IsNested: false),
            ],
            model.DeclaredTypes);
    }

    [Fact]
    public void BaseListYieldsInheritsSites()
    {
        var model = CSharpSourceParser.Parse(
            """
            namespace NS
            {
                class Foo : MonoBehaviour, IBar { }
                class Baz : Other.Base { }
            }
            """);

        Assert.Equal(
            [
                new TypeRefSite("MonoBehaviour", "Foo : MonoBehaviour", RefKind.Inherits, "NS"),
                new TypeRefSite("IBar", "Foo : IBar", RefKind.Inherits, "NS"),
                new TypeRefSite("Other.Base", "Baz : Other.Base", RefKind.Inherits, "NS"),
            ],
            model.TypeRefs);
    }

    [Fact]
    public void FieldKindFollowsUnitySerializationRule()
    {
        var model = CSharpSourceParser.Parse(
            """
            class C
            {
                public Foo publicField;
                [SerializeField] private Bar tagged;
                [UnityEngine.SerializeReference] private IThing byRef;
                private Baz invisible;
                public static Foo notSerializedStatic;
                public const int SkippedConst = 1;
                public readonly Foo notSerializedReadonly;
                public Foo Property { get; set; }
                public Qux a, b;
            }
            """);

        // Properties never yield a site; const int is a predefined type. Everything else
        // is a field site whose kind encodes serialized vs. not.
        Assert.Equal(
            [
                ("Foo", "C.publicField", RefKind.SerializedField),
                ("Bar", "C.tagged", RefKind.SerializedField),
                ("IThing", "C.byRef", RefKind.SerializedField),
                ("Baz", "C.invisible", RefKind.NonserializedField),
                ("Foo", "C.notSerializedStatic", RefKind.NonserializedField),
                ("Foo", "C.notSerializedReadonly", RefKind.NonserializedField),
                ("Qux", "C.a", RefKind.SerializedField),
                ("Qux", "C.b", RefKind.SerializedField),
            ],
            model.TypeRefs.Select(r => (r.TypeText, r.PropertyPath, r.Kind)));
    }

    [Fact]
    public void FieldTypeWrappersAreUnwrapped()
    {
        var model = CSharpSourceParser.Parse(
            """
            class C
            {
                public Foo[] array;
                public List<Bar> list;
                public NS.Qux qualified;
                public int predefined;
            }
            """);

        // The generic wrapper name itself is also collected ("List" resolves nowhere in
        // practice); predefined types never produce a site.
        Assert.Equal(
            ["Foo", "List", "Bar", "NS.Qux"],
            model.TypeRefs.Select(r => r.TypeText));
    }

    [Fact]
    public void GetComponentFamilyYieldsSites()
    {
        var model = CSharpSourceParser.Parse(
            """
            [RequireComponent(typeof(Rigidbody), typeof(Collider))]
            class C : MonoBehaviour
            {
                void M(GameObject go)
                {
                    GetComponent<Foo>();
                    go.TryGetComponent<Bar>(out var bar);
                    go?.GetComponentInChildren<Baz>();
                    gameObject.AddComponent(typeof(Qux));
                    FindFirstObjectByType<Quux>();
                    NotAComponentCall<Skipped>();
                }
            }
            """);

        var sites = model.TypeRefs.Where(r => r.Kind == RefKind.GetComponent).ToList();
        Assert.Equal(
            [
                ("Rigidbody", "C.[RequireComponent(typeof(Rigidbody))]"),
                ("Collider", "C.[RequireComponent(typeof(Collider))]"),
                ("Foo", "C.GetComponent<Foo>"),
                ("Bar", "C.TryGetComponent<Bar>"),
                ("Baz", "C.GetComponentInChildren<Baz>"),
                ("Qux", "C.AddComponent(typeof(Qux))"),
                ("Quux", "C.FindFirstObjectByType<Quux>"),
            ],
            sites.Select(r => (r.TypeText, r.PropertyPath)));
        Assert.DoesNotContain(model.TypeRefs, r => r.TypeText == "Skipped");
    }

    [Fact]
    public void CustomEditorAttributeYieldsSite()
    {
        var model = CSharpSourceParser.Parse(
            """
            [CustomEditor(typeof(CameraControllerSettings))]
            class SettingsEditor : Editor { }

            [UnityEditor.CustomEditor(typeof(Other), true)]
            class OtherEditor : Editor { }

            [CustomEditor(editorForChildClasses: true)]
            class NoTypeArg : Editor { }
            """);

        Assert.Equal(
            [
                ("CameraControllerSettings", "SettingsEditor.[CustomEditor(typeof(CameraControllerSettings))]"),
                ("Other", "OtherEditor.[CustomEditor(typeof(Other))]"),
            ],
            model.TypeRefs
                .Where(r => r.Kind == RefKind.CustomEditor)
                .Select(r => (r.TypeText, r.PropertyPath)));
    }

    [Fact]
    public void ResourcesLoadLiteralsAreCaptured()
    {
        var model = CSharpSourceParser.Parse(
            """
            class C
            {
                void M(string dynamicPath)
                {
                    Resources.Load("UI/Panel");
                    UnityEngine.Resources.Load<Sprite>("Icons/Sword");
                    Resources.LoadAll("Prefabs");
                    Resources.Load(dynamicPath);
                    Other.Load("not-resources");
                }
            }
            """);

        Assert.Equal(
            [
                new ResourceLoadSite("UI/Panel", """C.Resources.Load("UI/Panel")"""),
                new ResourceLoadSite("Icons/Sword", """C.Resources.Load("Icons/Sword")"""),
                new ResourceLoadSite("Prefabs", """C.Resources.LoadAll("Prefabs")"""),
            ],
            model.ResourceLoads);
        Assert.Equal(1, model.NonLiteralResourceLoads);
    }

    [Fact]
    public void UsingsAndAliasesAreCollected()
    {
        var model = CSharpSourceParser.Parse(
            """
            using A.B;
            using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
            using static F.G;

            namespace X
            {
                using C.D;
            }
            """);

        Assert.Equal(["A.B", "C.D"], model.Usings.Order());
        Assert.Equal("UnityEngine.InputSystem.EnhancedTouch.Touch", model.UsingAliases["Touch"]);
        Assert.DoesNotContain("F.G", model.Usings);
    }

    /// <summary>
    /// The parser sees the editor side of #if splits (UNITY_EDITOR defined) and treats
    /// version gates the way a Unity 6000.0 editor would — the documented approximation.
    /// </summary>
    [Fact]
    public void PreprocessorTakesTheUnityEditorBranch()
    {
        var model = CSharpSourceParser.Parse(
            """
            #if UNITY_EDITOR
            class EditorOnly { }
            #else
            class RuntimeOnly { }
            #endif
            #if UNITY_2021_2_OR_NEWER
            class Modern { }
            #endif
            #if UNITY_9999_9_OR_NEWER
            class Future { }
            #endif
            """);

        Assert.Equal(["EditorOnly", "Modern"], model.DeclaredTypes.Select(t => t.Name));
    }
}
