using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MesabrookAPIQueryTool
{
    public static class Config
    {
        internal const string LocalRedirectURI = "http://localhost:39420";

        static Config()
        {
            if (!File.Exists("config.json"))
            {
                File.WriteAllText("config.json", "{}");
            }

            JsonDocument document = JsonDocument.Parse(File.ReadAllText("config.json"));
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("ClientID", out JsonElement clientIDProperty))
            {
                ClientID = clientIDProperty.GetString();
            }

            if (root.TryGetProperty("AuthToken", out JsonElement authTokenElement))
            {
                AuthToken = authTokenElement.GetString();
            }

            if (root.TryGetProperty("RefreshToken", out JsonElement refreshTokenElement))
            {
                RefreshToken = refreshTokenElement.GetString();
            }
        }

        public static string? ClientID { get; set; }
        public static string? AuthToken { get; set; }
        public static string? RefreshToken { get; set; }

        public static void Save()
        {
            JsonObject rootNode = new JsonObject();
            rootNode["ClientID"] = ClientID;
            rootNode["AuthToken"] = AuthToken;
            rootNode["RefreshToken"] = RefreshToken;
            File.WriteAllText("config.json", rootNode.ToJsonString());
        }
    }
}
