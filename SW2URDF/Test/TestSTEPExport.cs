using Moq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Xunit;

namespace SW2URDF.Test
{
    [Collection("Requires SW Test Collection")]
    public class TestSTEPExport : SW2URDFTest
    {
        public TestSTEPExport(SWTestFixture fixture) : base(fixture) { }

        private ExportHelper Prepare(string modelName, out List<string> failures)
        {
            ModelDoc2 model = OpenSWDocument(modelName);
            var messages = new List<string>();
            var messageBox = new Mock<IMessageBox>();
            messageBox.Setup(m => m.Show(It.IsAny<string>())).Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messageBox.Object;
            ExportHelper helper = new ExportHelper(SwApp);
            helper.StepExportMessageBox = messageBox.Object;
            messageBox.Setup(m => m.Show(It.Is<string>(s => s.StartsWith("The original export completed"))))
                .Callback<string>(messages.Add).Returns(MessageBoxResult.OK);
            helper.SavePath = CreateRandomTempDirectory();
            helper.PackageName = modelName + "_description";
            LinkNode node = ConfigurationSerialization.LoadBaseNodeFromModel(model, out bool error);
            Assert.False(error);
            var missingLinks = new List<string>();
            CommonSwOperations.LoadSWComponents(model, node, missingLinks);
            Assert.Empty(missingLinks);
            helper.CreateRobotFromTreeView(node);
            failures = messages;
            return helper;
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        [InlineData("ORIGINAL_3_DOF_ARM")]
        public void TestSTLHasMatchingSTEP(string modelName)
        {
            ExportHelper helper = Prepare(modelName, out var failures);
            bool atomicSave = SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAtomicSave);
            helper.ExportRobot();
            Assert.Empty(failures);
            Assert.Equal(atomicSave, SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAtomicSave));
            string directory = Path.Combine(helper.SavePath, helper.PackageName, "meshes");
            string[] meshes = Directory.GetFiles(directory, "*.STL");
            Assert.NotEmpty(meshes);
            Assert.Equal(meshes.Length, Directory.GetFiles(directory, "*.STEP").Length);
            foreach (string mesh in meshes)
            {
                string step = Path.ChangeExtension(mesh, ".STEP");
                Assert.Contains("ISO-10303-21", File.ReadAllText(step));
                Assert.Contains("MANIFOLD_SOLID_BREP", File.ReadAllText(step));
                // The examples contain curved faces and use the upstream coarse STL tessellation.
                AssertBoundsMatch(mesh, step, 5e-4);
            }
            Assert.DoesNotContain(".STEP", File.ReadAllText(Path.Combine(helper.SavePath, helper.PackageName,
                "urdf", helper.PackageName + ".urdf")));
            SwApp.CloseAllDocuments(true);
            Console.WriteLine("STEP test output: " + Path.Combine(helper.SavePath, helper.PackageName));
        }

        private void AssertBoundsMatch(string mesh, string step, double tolerance = 1e-5, int expectedBodies = 0)
        {
            var meshVertices = new List<double[]>();
            double[] meshBounds = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
                double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            using (var reader = new BinaryReader(File.OpenRead(mesh)))
            {
                reader.ReadBytes(80);
                uint triangles = reader.ReadUInt32();
                Assert.True(triangles > 0);
                for (uint t = 0; t < triangles; t++)
                {
                    reader.ReadBytes(12);
                    for (int v = 0; v < 3; v++)
                    {
                        double[] vertex = new double[3];
                        for (int axis = 0; axis < 3; axis++)
                        {
                            double value = reader.ReadSingle();
                            vertex[axis] = value;
                            meshBounds[axis] = Math.Min(meshBounds[axis], value);
                            meshBounds[axis + 3] = Math.Max(meshBounds[axis + 3], value);
                        }
                        if (expectedBodies > 0) meshVertices.Add(vertex);
                    }
                    reader.ReadUInt16();
                }
            }
            int errors = 0;
            object importData = SwApp.GetImportFileData(step);
            ModelDoc2 imported = SwApp.LoadFile4(step, "r", importData, ref errors);
            Assert.NotNull(imported);
            try
            {
                var bodies = new List<Body2>();
                if (imported.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    bodies.AddRange(((object[])((PartDoc)imported).GetBodies2((int)swBodyType_e.swSolidBody, false))
                        .Cast<Body2>());
                }
                else
                {
                    foreach (Component2 component in (object[])((AssemblyDoc)imported).GetComponents(false))
                    {
                        if (component.GetBodies3((int)swBodyType_e.swSolidBody, out _) is object[] componentBodies)
                        {
                            foreach (Body2 body in componentBodies)
                            {
                                Body2 copy = (Body2)body.Copy();
                                Assert.True(copy.ApplyTransform(component.Transform2));
                                bodies.Add(copy);
                            }
                        }
                    }
                }
                Assert.NotEmpty(bodies);
                if (expectedBodies > 0)
                {
                    Assert.Equal(expectedBodies, bodies.Count);
                    foreach (Body2 body in bodies)
                    {
                        foreach (Vertex vertex in (object[])body.GetVertices())
                        {
                            double[] point = vertex.GetPoint();
                            Assert.True(meshVertices.Any(v => Enumerable.Range(0, 3).Sum(axis =>
                                (v[axis] - point[axis]) * (v[axis] - point[axis])) <= tolerance * tolerance),
                                "A planar STEP corner was missing from STL: " + string.Join(", ", point));
                        }
                    }
                }
                double maximumDifference = 0;
                for (int side = 0; side < 6; side++)
                {
                    int axis = side % 3;
                    double[] direction = new double[3];
                    direction[axis] = side < 3 ? -1 : 1;
                    double bound = side < 3 ? double.PositiveInfinity : double.NegativeInfinity;
                    foreach (Body2 body in bodies)
                    {
                        Assert.True(body.GetExtremePoint(direction[0], direction[1], direction[2],
                            out double x, out double y, out double z));
                        double point = new[] { x, y, z }[axis];
                        bound = side < 3 ? Math.Min(bound, point) : Math.Max(bound, point);
                    }
                    double difference = Math.Abs(bound - meshBounds[side]);
                    maximumDifference = Math.Max(maximumDifference, difference);
                    Assert.True(difference <= tolerance, Path.GetFileName(step) + " bound " + side +
                        ": STEP=" + bound + " m, STL=" + meshBounds[side] + " m, difference=" + difference + " m");
                }
                Console.WriteLine(Path.GetFileName(step) + ": " + bodies.Count +
                    " CAD bodies, max STEP/STL bound difference=" + maximumDifference + " m");
            }
            finally
            {
                SwApp.CloseDoc(imported.GetTitle());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestAsymmetricPart(bool zIsUp)
        {
            SwApp.CloseAllDocuments(true);
            string template = SwApp.GetDocumentTemplate((int)swDocumentTypes_e.swDocPART, "", 0, 0, 0);
            if (string.IsNullOrEmpty(template) || !File.Exists(template))
            {
                template = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                    "SOLIDWORKS", "SOLIDWORKS 2025", "templates", "gb_part.prtdot");
            }
            ModelDoc2 model = (ModelDoc2)SwApp.NewDocument(template, 0, 0, 0);
            Assert.NotNull(model);
            Modeler modeler = (Modeler)SwApp.GetModeler();
            Body2 box = modeler.CreateBodyFromBox3(new double[] { 0.12, -0.07, 0.031, 0, 0, 1, 0.037, 0.061, 0.023 });
            Assert.NotNull(box);
            Assert.NotNull(((PartDoc)model).CreateFeatureFromBody3(box, false, 0));
            var messages = new Mock<IMessageBox>();
            messages.Setup(m => m.Show(It.IsAny<string>())).Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messages.Object;
            var helper = new ExportHelper(SwApp);
            helper.StepExportMessageBox = messages.Object;
            helper.CreateRobotFromActiveModel();
            helper.PackageName = "asymmetric_part_" + zIsUp;
            helper.SavePath = CreateRandomTempDirectory();
            helper.URDFRobot.BaseLink.Name = "offset_box";
            helper.ExportLink(zIsUp);
            messages.Verify(m => m.Show(It.Is<string>(s => s.StartsWith("The original export completed"))), Times.Never);
            string mesh = Path.Combine(helper.SavePath, helper.PackageName, "meshes", "offset_box.STL");
            AssertBoundsMatch(mesh, Path.ChangeExtension(mesh, ".STEP"), 1e-5, 1);
            // The STEP helper must preserve a caller's selection, including when saving over an existing file.
            model.ClearSelection2(true);
            Body2 selectedBody = (Body2)((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, true))[0];
            Assert.True(selectedBody.Select2(false, null));
            typeof(ExportHelper).GetMethod("TrySaveSTEP", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(helper, new object[] { helper.URDFRobot.BaseLink, mesh });
            Assert.Equal(1, ((SelectionMgr)model.SelectionManager).GetSelectedObjectCount2(-1));
            SwApp.CloseAllDocuments(true);
        }

        [Theory]
        [InlineData(false, MeshExportFormat.STL)]
        [InlineData(true, MeshExportFormat.THREEDXML)]
        public void TestOtherModesDoNotCreateSTEP(bool exportMeshes, MeshExportFormat format)
        {
            ExportHelper helper = Prepare("3_DOF_ARM", out var failures);
            helper.ExportRobot(exportMeshes, format);
            Assert.Empty(failures);
            Assert.Empty(Directory.GetFiles(Path.Combine(helper.SavePath, helper.PackageName, "meshes"), "*.STEP"));
            SwApp.CloseAllDocuments(true);
        }

        [Fact]
        public void TestFailureKeepsOldFileAndContinues()
        {
            ExportHelper helper = Prepare("4_WHEELER", out var failures);
            var assembly = (AssemblyDoc)helper.ActiveSWModel;
            object[] components = assembly.GetComponents(false);
            var hidden = CommonSwOperations.FindHiddenComponents(components);
            bool atomicSave = SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAtomicSave);
            bool stlBinary = SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat);
            string directory = Path.Combine(helper.SavePath, helper.PackageName, "meshes");
            Directory.CreateDirectory(directory);
            string lockedFile = Path.Combine(directory, "back_left_link.STEP");
            File.WriteAllText(lockedFile, "previous STEP");
            try
            {
                SwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAtomicSave, true);
                using (File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    helper.ExportRobot();
                }
                Assert.Single(failures);
                Assert.Contains("back_left_link", failures[0]);
                Assert.Contains("previous STEP file was not updated", failures[0]);
                Assert.Equal("previous STEP", File.ReadAllText(lockedFile));
                Assert.Equal(Directory.GetFiles(directory, "*.STL").Length, Directory.GetFiles(directory, "*.STEP").Length);
                Assert.Contains("ISO-10303-21", File.ReadAllText(Path.Combine(directory, "base_link.STEP")));
                Assert.Empty(Directory.GetFiles(directory, ".*.STEP"));
                Assert.True(SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAtomicSave));
                Assert.Equal(stlBinary, SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat));
                components = assembly.GetComponents(false);
                Assert.Equal(hidden.OrderBy(s => s), CommonSwOperations.FindHiddenComponents(components).OrderBy(s => s));
                Assert.Contains("previous STEP file was not updated", File.ReadAllText(Path.Combine(helper.SavePath, helper.PackageName, "export.log")));
                Assert.True(File.Exists(Path.Combine(helper.SavePath, helper.PackageName, "urdf", helper.PackageName + ".csv")));
            }
            finally
            {
                SwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAtomicSave, atomicSave);
                SwApp.CloseAllDocuments(true);
            }
        }

        [Fact]
        public void TestReferenceFrameDoesNotCreateSTEP()
        {
            ExportHelper helper = Prepare("3_DOF_ARM", out var failures);
            var frame = new Link(helper.URDFRobot.BaseLink) { Name = "reference_frame", isFixedFrame = true };
            helper.URDFRobot.BaseLink.Children.Add(frame);
            helper.ExportRobot();
            Assert.Empty(failures);
            string directory = Path.Combine(helper.SavePath, helper.PackageName, "meshes");
            Assert.False(File.Exists(Path.Combine(directory, "reference_frame.STEP")));
            Assert.False(File.Exists(Path.Combine(directory, "reference_frame.STL")));
            SwApp.CloseAllDocuments(true);
        }

        private ModelDoc2 NewDocument(string templateName)
        {
            string template = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                "SOLIDWORKS", "SOLIDWORKS 2025", "templates", templateName);
            ModelDoc2 model = (ModelDoc2)SwApp.NewDocument(template, 0, 0, 0);
            Assert.NotNull(model);
            return model;
        }

        private static void SaveModel(ModelDoc2 model, string filename)
        {
            int errors = 0, warnings = 0;
            Assert.True(model.Extension.SaveAs(filename, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null, ref errors, ref warnings));
            Assert.Equal(0, errors);
        }

        private Component2 InsertComponent(ModelDoc2 assembly, string filename, double angle, double x, double y, double z)
        {
            Component2 component = ((AssemblyDoc)assembly).AddComponent5(filename, 0, "", false, "", x, y, z);
            Assert.NotNull(component);
            MathUtility math = SwApp.GetMathUtility();
            component.Transform2 = math.CreateTransform(new double[] {
                Math.Cos(angle), Math.Sin(angle), 0, -Math.Sin(angle), Math.Cos(angle), 0, 0, 0, 1,
                x, y, z, 1, 0, 0, 0 });
            return component;
        }

        [Fact]
        public void TestRepeatedMultibodyComponentsAndRotatedFrame()
        {
            SwApp.CloseAllDocuments(true);
            string directory = CreateRandomTempDirectory();
            ModelDoc2 part = NewDocument("gb_part.prtdot");
            Modeler modeler = SwApp.GetModeler();
            foreach (double x in new[] { 0.12, 0.21 })
            {
                Body2 box = modeler.CreateBodyFromBox3(new double[] { x, -0.07, 0.031, 0, 0, 1, 0.037, 0.061, 0.023 });
                Assert.NotNull(((PartDoc)part).CreateFeatureFromBody3(box, false, 0));
            }
            string partFile = Path.Combine(directory, "two_boxes.SLDPRT");
            SaveModel(part, partFile);
            ModelDoc2 subassembly = NewDocument("gb_assembly.asmdot");
            InsertComponent(subassembly, partFile, 0.4, 0.08, -0.2, 0.1);
            InsertComponent(subassembly, partFile, -0.7, -0.3, 0.17, -0.08);
            string subassemblyFile = Path.Combine(directory, "repeated.SLDASM");
            SaveModel(subassembly, subassemblyFile);
            ModelDoc2 assembly = NewDocument("gb_assembly.asmdot");
            Component2 nested = InsertComponent(assembly, subassemblyFile, 0.9, 0.23, 0.15, -0.12);
            Component2 direct = InsertComponent(assembly, partFile, -0.35, -0.22, -0.1, 0.3);
            InsertComponent(assembly, partFile, 0, 3, 4, 5); // Another link: must never enter this STEP.
            var messages = new Mock<IMessageBox>();
            messages.Setup(m => m.Show(It.IsAny<string>())).Returns(MessageBoxResult.OK);
            URDFPackage.MessageBox = messages.Object;
            var helper = new ExportHelper(SwApp) { SavePath = directory, PackageName = "planar_assembly" };
            helper.StepExportMessageBox = messages.Object;
            var link = new Link { Name = "six_bodies" };
            link.SWComponents.Add(nested);
            foreach (Component2 child in (object[])nested.GetChildren())
            {
                link.SWComponents.Add(child); // Upstream visibility requires explicit children; recursion must deduplicate them.
            }
            link.SWComponents.Add(direct);
            helper.URDFRobot = new Robot();
            helper.URDFRobot.SetBaseLink(link);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string sketch = (string)typeof(ExportHelper).GetMethod("Setup3DSketch", flags).Invoke(helper, null);
            typeof(ExportHelper).GetField("referenceSketchName", flags).SetValue(helper, sketch);
            var origin = new Origin(true);
            origin.SetXYZ(new[] { 0.17, -0.13, 0.09 });
            origin.SetRPY(new[] { 0.21, -0.32, 0.63 });
            typeof(ExportHelper).GetMethod("CreateRefOrigin", flags, null, new[] { typeof(Origin), typeof(string) }, null)
                .Invoke(helper, new object[] { origin, "validation_frame" });
            link.Joint.CoordinateSystemName = "validation_frame";
            helper.ExportRobot();
            messages.Verify(m => m.Show(It.Is<string>(s => s.StartsWith("The original export completed"))), Times.Never);
            string mesh = Path.Combine(directory, helper.PackageName, "meshes", "six_bodies.STL");
            AssertBoundsMatch(mesh, Path.ChangeExtension(mesh, ".STEP"), 1e-5, 6);
            Console.WriteLine("Planar assembly output: " + directory);
            SwApp.CloseAllDocuments(true);
        }

        [Theory]
        [InlineData("3_DOF_ARM")]
        [InlineData("4_WHEELER")]
        [InlineData("ORIGINAL_3_DOF_ARM")]
        public void TestUpstreamRegression(string modelName)
        {
            string baselineFile = System.Environment.GetEnvironmentVariable("SW2URDF_BASELINE_DLL");
            if (string.IsNullOrEmpty(baselineFile))
            {
                Console.WriteLine("Set SW2URDF_BASELINE_DLL to run the optional unmodified-upstream comparison.");
                return;
            }
            Assembly baseline = Assembly.LoadFile(Path.GetFullPath(baselineFile));
            // Both copies use the same log4net repository; keep the current test log appender.
            baseline.GetType("SW2URDF.Utilities.Logger").GetField("Initialized", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
            ModelDoc2 model = OpenSWDocument(modelName);
            Type baselineMessage = baseline.GetType("SW2URDF.UI.IMessageBox", true);
            object mock = Activator.CreateInstance(typeof(Mock<>).MakeGenericType(baselineMessage));
            object messageBox = mock.GetType().GetProperty("Object", baselineMessage).GetValue(mock);
            baseline.GetType("SW2URDF.URDFExport.URDFPackage").GetField("MessageBox").SetValue(null, messageBox);
            Type helperType = baseline.GetType("SW2URDF.URDFExport.ExportHelper", true);
            object baselineHelper = Activator.CreateInstance(helperType, new object[] { SwApp });
            object[] loadArgs = { model, false };
            object node = baseline.GetType("SW2URDF.URDFExport.ConfigurationSerialization")
                .GetMethod("LoadBaseNodeFromModel").Invoke(null, loadArgs);
            Assert.False((bool)loadArgs[1]);
            var missing = new List<string>();
            baseline.GetType("SW2URDF.URDFExport.CommonSwOperations").GetMethods()
                .Single(m => m.Name == "LoadSWComponents" && m.GetParameters()[1].ParameterType == node.GetType())
                .Invoke(null, new object[] { model, node, missing });
            Assert.Empty(missing);
            helperType.GetMethod("CreateRobotFromTreeView").Invoke(baselineHelper, new[] { node });
            string baselineDirectory = CreateRandomTempDirectory();
            string packageName = modelName + "_description";
            helperType.GetProperty("SavePath").SetValue(baselineHelper, baselineDirectory);
            helperType.GetProperty("PackageName").SetValue(baselineHelper, packageName);
            helperType.GetMethod("ExportRobot").Invoke(baselineHelper, new object[] { true, 0 });
            SwApp.CloseAllDocuments(true);
            ExportHelper helper = Prepare(modelName, out var failures);
            helper.ExportRobot();
            Assert.Empty(failures);
            string currentPackage = Path.Combine(helper.SavePath, packageName);
            string baselinePackage = Path.Combine(baselineDirectory, packageName);
            foreach (string mesh in Directory.GetFiles(Path.Combine(baselinePackage, "meshes"), "*.STL"))
            {
                // SolidWorks can reorder component triangles after reopening an assembly.
                var before = ReadSortedSTLTriangles(mesh);
                var after = ReadSortedSTLTriangles(Path.Combine(currentPackage, "meshes", Path.GetFileName(mesh)));
                Assert.Equal(before.Count, after.Count);
                double maximum = before.SelectMany((triangle, i) => triangle.Select((value, j) =>
                    Math.Abs(value - after[i][j]))).Max();
                Assert.True(maximum <= 1e-7, Path.GetFileName(mesh) + " STL maximum difference: " + maximum);
                Console.WriteLine(modelName + "/" + Path.GetFileName(mesh) + " STL max difference=" + maximum);
            }
            AssertNumericallyEqual(File.ReadAllText(Path.Combine(baselinePackage, "urdf", packageName + ".csv")),
                File.ReadAllText(Path.Combine(currentPackage, "urdf", packageName + ".csv")));
            // The version comment changes; all robot elements (including inertia and joint transforms) must match.
            AssertNumericallyEqual(System.Xml.Linq.XDocument.Load(Path.Combine(baselinePackage, "urdf", packageName + ".urdf")).Root.ToString(),
                System.Xml.Linq.XDocument.Load(Path.Combine(currentPackage, "urdf", packageName + ".urdf")).Root.ToString());
            Console.WriteLine("Upstream STL/URDF/CSV regression passed: " + modelName);
            SwApp.CloseAllDocuments(true);
        }

        private static void AssertNumericallyEqual(string expected, string actual)
        {
            // Reopening a model can change insignificant mass-property roundoff in SolidWorks itself.
            const string number = @"([-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[Ee][-+]?\d+)?)";
            string[] before = System.Text.RegularExpressions.Regex.Split(expected, number);
            string[] after = System.Text.RegularExpressions.Regex.Split(actual, number);
            Assert.Equal(before.Length, after.Length);
            for (int i = 0; i < before.Length; i++)
            {
                if (i % 2 == 0) Assert.Equal(before[i], after[i]);
                else
                {
                    double x = double.Parse(before[i], System.Globalization.CultureInfo.InvariantCulture);
                    double y = double.Parse(after[i], System.Globalization.CultureInfo.InvariantCulture);
                    Assert.True(Math.Abs(x - y) <= 1e-10 * Math.Max(1, Math.Abs(x)),
                        "Numeric regression: " + before[i] + " != " + after[i]);
                }
            }
        }

        private static List<double[]> ReadSortedSTLTriangles(string filename)
        {
            var triangles = new List<double[]>();
            Comparison<double[]> compare = (x, y) =>
            {
                for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                {
                    int result = Math.Round(x[i], 6).CompareTo(Math.Round(y[i], 6));
                    if (result != 0) return result;
                }
                return 0;
            };
            using (var reader = new BinaryReader(File.OpenRead(filename)))
            {
                reader.ReadBytes(80);
                uint count = reader.ReadUInt32();
                for (uint i = 0; i < count; i++)
                {
                    double[] normal = { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() };
                    var vertices = new List<double[]>();
                    for (int j = 0; j < 3; j++) vertices.Add(new double[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() });
                    vertices.Sort(compare);
                    triangles.Add(vertices.SelectMany(v => v).Concat(normal).ToArray());
                    reader.ReadUInt16();
                }
            }
            triangles.Sort(compare);
            return triangles;
        }
    }
}
