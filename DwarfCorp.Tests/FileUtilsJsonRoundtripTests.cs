using System.IO;
using Microsoft.Xna.Framework;
using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// Pins save-serialization compatibility. The migration to MonoGame must not
/// change the JSON format that existing .save/.meta/.chunk files use. If the
/// Newtonsoft Converters (Vector3Converter, PointConverter, etc.) ever get
/// swapped for a faster serializer, this test has to keep passing first.
/// </summary>
public class FileUtilsJsonRoundtripTests
{
    public class SimplePayload
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public Vector3 Position { get; set; }
        public Point TilePos { get; set; }
    }

    [Fact]
    public void SaveJson_ThenLoad_ReturnsEquivalentObject()
    {
        var original = new SimplePayload
        {
            Name = "TestDwarf",
            Count = 42,
            Position = new Vector3(1.5f, -2.25f, 3.75f),
            TilePos = new Point(10, 20)
        };

        string tempFile = Path.Combine(Path.GetTempPath(), $"dwarfcorp_tests_{System.Guid.NewGuid():N}.json");
        try
        {
            Assert.True(FileUtils.SaveJSON(original, tempFile));
            Assert.True(File.Exists(tempFile));

            var loaded = FileUtils.LoadJsonFromAbsolutePath<SimplePayload>(tempFile);
            Assert.NotNull(loaded);
            Assert.Equal(original.Name, loaded.Name);
            Assert.Equal(original.Count, loaded.Count);
            Assert.Equal(original.Position, loaded.Position);
            Assert.Equal(original.TilePos, loaded.TilePos);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveJson_ProducesHumanReadableJson_NotBinaryBlob()
    {
        var payload = new SimplePayload { Name = "x", Count = 1, Position = Vector3.Zero, TilePos = Point.Zero };
        string tempFile = Path.Combine(Path.GetTempPath(), $"dwarfcorp_tests_{System.Guid.NewGuid():N}.json");
        try
        {
            FileUtils.SaveJSON(payload, tempFile);
            string text = File.ReadAllText(tempFile);
            // Basic human-readable JSON shape. This is what lets players share/inspect saves.
            Assert.Contains("\"Name\"", text);
            Assert.Contains("\"x\"", text);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
