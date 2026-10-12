using System.Collections.Generic;
using NUnit.Framework;

namespace UnityNvim.Editor.Tests
{
    public class JsonTests
    {
        [Test]
        public void RoundTrip()
        {
            var obj = new Dictionary<string, object>
            {
                ["s"] = "a \"quoted\" \\ line\nnext\t\u0001",
                ["n"] = 12,
                ["f"] = 1.5,
                ["b"] = true,
                ["nil"] = null,
                ["list"] = new List<object> { 1, "two", false },
            };
            var parsed = (Dictionary<string, object>)Json.Parse(Json.Serialize(obj));
            Assert.AreEqual(obj["s"], parsed["s"]);
            Assert.AreEqual(12.0, parsed["n"]);
            Assert.AreEqual(1.5, parsed["f"]);
            Assert.AreEqual(true, parsed["b"]);
            Assert.IsNull(parsed["nil"]);
            CollectionAssert.AreEqual(new List<object> { 1.0, "two", false }, (List<object>)parsed["list"]);
        }

        [Test]
        public void ParsesClientHello()
        {
            var msg = (Dictionary<string, object>)Json.Parse("{\"type\":\"hello\",\"token\":\"ab\",\"logs\":true,\"id\":3,\"u\":\"\\u00e7\"}");
            Assert.AreEqual("hello", msg.Str("type"));
            Assert.AreEqual(true, msg.Bool("logs"));
            Assert.AreEqual(3, msg.Int("id"));
            Assert.AreEqual("ç", msg.Str("u"));
        }

        [Test]
        public void RejectsGarbage()
        {
            Assert.Throws<System.FormatException>(() => Json.Parse("{\"a\":}"));
            Assert.Throws<System.FormatException>(() => Json.Parse("[1,2"));
        }
    }
}
