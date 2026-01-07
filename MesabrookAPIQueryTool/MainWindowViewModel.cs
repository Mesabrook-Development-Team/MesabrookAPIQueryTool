using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MesabrookAPIQueryTool.Extensions;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Input;

namespace MesabrookAPIQueryTool
{
    public class MainWindowViewModel : ObservableObject
    {
        private bool _isInitialized;
        public bool IsInitialized
        {
            get => _isInitialized;
            set => SetProperty(ref _isInitialized, value);
        }

        public ICommand InitializeCommand => new AsyncRelayCommand(Initialize);

        private async Task Initialize()
        {
            if (string.IsNullOrEmpty(Config.ClientID))
            {
                RegisterWindow registerWindow = new RegisterWindow();
                registerWindow.Topmost = true;
                registerWindow.ShowDialog();
            }

            if (string.IsNullOrEmpty(Config.ClientID))
            {
                App.Current.Shutdown();
                return;
            }

            if (string.IsNullOrEmpty(Config.AuthToken) || string.IsNullOrEmpty(Config.RefreshToken))
            {
                IsSignedIn = false;
            }
            else
            {
                List<QueryResponseObject>? response = await ExecuteQuery(APIs.System, "Program/GetProgramKeys", false);
                IsSignedIn = response != null;
            }

            IsInitialized = true;
        }

        private bool _isSignedIn;
        public bool IsSignedIn
        {
            get => _isSignedIn;
            set => SetProperty(ref _isSignedIn, value);
        }

        public ICommand SignInCommand => new RelayCommand(SignIn);

        private void SignIn()
        {
            if (IsSignedIn)
            {
                return;
            }

            LoginWindow login = new LoginWindow();
            bool? result = login.ShowDialog();
            IsSignedIn = result ?? false;
        }

        public ICommand SignOutCommand => new AsyncRelayCommand(SignOut);

        private async Task SignOut()
        {
            if (!IsSignedIn)
            {
                return;
            }

            HttpClient signOutClient = new HttpClient();
            signOutClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Config.AuthToken);
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "https://auth.mesabrook.com/Revoke");
            message.Content = new StringContent("reason=" + HttpUtility.HtmlEncode("Sign out"), new MediaTypeHeaderValue(MediaTypeNames.Application.FormUrlEncoded));
            HttpResponseMessage response;
            try
            {
                 response = await signOutClient.SendAsync(message);
            }
            catch(Exception ex)
            {
                MessageBox.Show("Could not sign out: " + ex.ToString());
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                string responseText;
                using (StreamReader reader = new StreamReader(response.Content.ReadAsStream()))
                {
                    responseText = reader.ReadToEnd();
                }

                MessageBox.Show("Could not sign out: " + responseText);
                return;
            }

            Config.AuthToken = null;
            Config.RefreshToken = null;
            Config.Save();
            IsSignedIn = false;
        }

        public enum APIs
        {
            Company,
            Fleet,
            Government,
            MCSync,
            System,
            Towing
        }

        public List<APIs> AllAPIs => Enum.GetValues<APIs>().ToList();

        private APIs _selectedAPI;
        public APIs SelectedAPI
        {
            get => _selectedAPI;
            set => SetProperty(ref _selectedAPI, value);
        }

        private string? _csvHeaders;
        public string? CSVHeaders
        {
            get => _csvHeaders;
            set => SetProperty(ref _csvHeaders, value);
        }

        private string? _queryPath;
        public string? QueryPath
        {
            get => _queryPath;
            set => SetProperty(ref _queryPath, value);
        }

        public ICommand ExecuteQueryCommand => new AsyncRelayCommand(ExecuteQuery);

        private async Task ExecuteQuery()
        {
            if (string.IsNullOrEmpty(QueryPath))
            {
                MessageBox.Show("Enter a query path", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            List<QueryResponseObject>? objects = await ExecuteQuery(SelectedAPI, QueryPath);
            QueryResults.Clear();
            if (objects != null)
            {
                QueryResults.AddRange(objects);
            }
            else
            {
                QueryResults.Add(new QueryResponseObject(new KeyValuePair<string, object?>("Query Complete", "No Results")));
            }
        }

        private async Task<List<QueryResponseObject>?> ExecuteQuery(APIs api, string query, bool requireAuthorization = true)
        {
            if (string.IsNullOrEmpty(Config.AuthToken))
            {
                return null;
            }

            StringBuilder builder = new StringBuilder("https://api.mesabrook.com/");
            builder.Append(api switch
            {
                APIs.Company => "company",
                APIs.System => "system",
                APIs.Government => "gov",
                APIs.Fleet => "fleet",
                APIs.Towing => "towing",
                APIs.MCSync => "mcsync",
                _ => throw new NotSupportedException()
            });
            builder.Append("/");
            string queryPath = query.StartsWith("/") ? query.Substring(1) : query;
            if (queryPath.Contains("?"))
            {
                queryPath = queryPath.Substring(0, queryPath.IndexOf("?"));
            }
            builder.Append(queryPath);

            UriBuilder uriBuilder = new UriBuilder(builder.ToString());

            // Query string
            if (query.Contains("?"))
            {
                NameValueCollection queryCollection = HttpUtility.ParseQueryString(uriBuilder.Query);

                string queryString = query.Substring(query.IndexOf("?") + 1);
                string[] queryParts = queryString.Split("&");
                foreach(string queryPart in queryParts)
                {
                    string[] kvp = queryPart.Split("=");
                    if (kvp.Length < 2)
                    {
                        continue;
                    }

                    queryCollection[kvp[0]] = kvp[1];
                }

                uriBuilder.Query = queryCollection.ToString();
            }

            string? jsonString = null;
            for (int i = 0; i < 2; i++)
            {
                if (requireAuthorization && !IsSignedIn)
                {
                    return null;
                }

                HttpClient client = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
                request.Headers.Add("Authorization", $"Bearer {Config.AuthToken}");
                if (!string.IsNullOrEmpty(CSVHeaders))
                {
                    string[] headerParts = CSVHeaders.Split(",");
                    foreach(string headerPart in headerParts)
                    {
                        string[] headerKVP = headerPart.Split('=');
                        if (headerKVP.Length < 2)
                        {
                            continue;
                        }

                        request.Headers.Add(headerKVP[0], headerKVP[1]);
                    }
                }
                HttpResponseMessage? response = null;
                try
                {
                    response = await client.SendAsync(request);

                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        jsonString = "{ \"Error\": \"You do not have permission to view this information\"}";
                        break;
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await PerformTokenRefresh();
                        continue;
                    }

                    using (StreamReader reader = new StreamReader(response.Content.ReadAsStream()))
                    {
                        jsonString = reader.ReadToEnd();
                    }

                    break;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("An error occurred during data fetch: " + ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }
            }

            if (jsonString == null)
            {
                return null;
            }

            JsonNode? json = JsonNode.Parse(jsonString);
            if (json == null)
            {
                return null;
            }

            Dictionary<string, object?> ConvertJNodeToDictionary(JsonNode node)
            {
                Dictionary<string, object?> values = new Dictionary<string, object?>();
                switch(node.GetValueKind())
                {
                    case JsonValueKind.String:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                    case JsonValueKind.Number:
                        values["Literal Value"] = node.AsValue().ToString();
                        break;
                    case JsonValueKind.Object:
                        foreach (KeyValuePair<string, JsonNode?> kvp in node.AsObject())
                        {
                            if (kvp.Value == null) continue;

                            if (kvp.Value.GetValueKind() == JsonValueKind.Object || kvp.Value.GetValueKind() == JsonValueKind.Array)
                            {
                                values[kvp.Key] = ConvertJNodeToDictionary(kvp.Value);
                            }
                            else
                            {
                                values[kvp.Key] = kvp.Value.AsValue()?.ToString();
                            }
                        }
                        break;
                    case JsonValueKind.Array:
                        JsonArray nodeArray = node.AsArray();
                        for(int i = 0; i < nodeArray.Count; i++)
                        {
                            JsonNode? arrayNode = nodeArray[i];
                            if (arrayNode == null) continue;

                            values["Array Item " + (i + 1)] = ConvertJNodeToDictionary(arrayNode);
                        }
                        break;
                }

                if (values.Count == 0)
                {
                    values[" "] = "No Results";
                }
                return values;
            };

            Dictionary<string, object?> responseObjectDictionary = ConvertJNodeToDictionary(json);
            return [.. responseObjectDictionary.Select(kvp => new QueryResponseObject(kvp))];
        }

        private async Task PerformTokenRefresh()
        {
            HttpClient refreshTokenClient = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://auth.mesabrook.com/token");
            string data = $"grant_type=refresh_token&refresh_token={HttpUtility.HtmlEncode(Config.RefreshToken)}";
            request.Content = new StringContent(data, new MediaTypeHeaderValue(MediaTypeNames.Application.FormUrlEncoded));

            HttpResponseMessage? response = null;
            try
            {
                response = await refreshTokenClient.SendAsync(request);
            }
            catch(Exception ex)
            {
                IsSignedIn = false;
                return;
            }

            string jsonString;
            using (StreamReader reader = new StreamReader(response.Content.ReadAsStream()))
            {
                jsonString = reader.ReadToEnd();
            }

            JsonNode? json = JsonNode.Parse(jsonString);
            if (json == null || 
                json.GetValueKind() != JsonValueKind.Object ||
                !json.AsObject().TryGetPropertyValue("access_token", out JsonNode? accessTokenNode) ||
                !json.AsObject().TryGetPropertyValue("refresh_token", out JsonNode? refreshTokenNode))
            {
                IsSignedIn = false;
                return;
            }

            Config.AuthToken = accessTokenNode.GetValue<string>();
            Config.RefreshToken = refreshTokenNode.GetValue<string>();
            Config.Save();
        }

        private Dictionary<string, object>? _sampleResults = new Dictionary<string, object>()
        {
            ["RailcarID"] = 1L,
            ["ReportingMark"] = "CSX8600",
            ["Tests"] = new Dictionary<string, object>()
            {
                ["Test1"] = "Value1",
                ["Test2"] = 2,
                ["Test3"] = true
            }
        };

        private ObservableCollection<QueryResponseObject> _queryResults = new ObservableCollection<QueryResponseObject>();
        public ObservableCollection<QueryResponseObject> QueryResults
        {
            get => _queryResults;
            set => SetProperty(ref _queryResults, value);
        }

        private bool _areNodesExpanded;
        public bool AreNodesExpanded
        {
            get => _areNodesExpanded;
            set => SetProperty(ref _areNodesExpanded, value);
        }

        public ICommand ToggleNodeExpandedCommand => new RelayCommand(() => AreNodesExpanded = !AreNodesExpanded);
    }
}
