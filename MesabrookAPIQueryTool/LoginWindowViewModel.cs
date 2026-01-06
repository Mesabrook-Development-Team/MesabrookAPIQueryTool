using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using System.Windows;
using System.Windows.Input;

namespace MesabrookAPIQueryTool
{
    public class LoginWindowViewModel : ObservableObject
    {
        private Action _closeFailAction;
        private Action _closeSucceedAction;

        public LoginWindowViewModel(Action closeFailAction, Action closeSucceedAction)
        {
            _closeFailAction = closeFailAction;
            _closeSucceedAction = closeSucceedAction;
        }

        private string _loginURL = "about:blank";
        public string LoginURL
        {
            get => _loginURL;
            set => SetProperty(ref _loginURL, value);
        }


        public ICommand WindowLoadedCommand => new RelayCommand(WindowLoaded);

        private void WindowLoaded()
        {
            string loginState = Guid.NewGuid().ToString();
            ListenForLogin(loginState);
            LoginURL = $"https://auth.mesabrook.com/authorize?response_type=code&client_id={HttpUtility.UrlEncode(Config.ClientID)}" +
                $"&redirect_uri={HttpUtility.UrlEncode(Config.LocalRedirectURI)}&state={HttpUtility.UrlEncode(loginState)}";
        }

        private CancellationTokenSource? _loginTokenSource = null;
        private async void ListenForLogin(string state)
        {
            if (_loginTokenSource != null)
            {
                _loginTokenSource.Cancel();
            }

            _loginTokenSource = new CancellationTokenSource();
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add($"{Config.LocalRedirectURI}/");

            try
            {
                listener.Start();
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Failed to start local HTTP listener on {Config.LocalRedirectURI} for login.\n\nError details: {ex.Message}", "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _closeFailAction();
                return;
            }

            string? code = null;
            string? returnedState = null;
            while(true)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(_loginTokenSource.Token);
                    NameValueCollection queryParts = context.Request.QueryString;

                    if (!queryParts.AllKeys.Contains("code") || !queryParts.AllKeys.Contains("state"))
                    {
                        continue;
                    }

                    code = queryParts.Get("code");
                    returnedState = queryParts.Get("state");
                    break;
                }
                catch(TaskCanceledException ex)
                {
                    break;
                }
                finally
                {
                    context?.Response.Close();
                }
            }

            listener.Stop();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(returnedState))
            {
                return;
            }

            if (!string.Equals(state, returnedState))
            {
                MessageBox.Show("Login state tamper has been detected. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _closeFailAction();
                return;
            }

            HandleCode(code);
        }

        private async void HandleCode(string code)
        {
            NameValueCollection tokenData = new NameValueCollection();
            tokenData["grant_type"] = "authorization_code";
            tokenData["code"] = code;
            tokenData["redirect_uri"] = Config.LocalRedirectURI;
            tokenData["client_id"] = Config.ClientID;
            StringBuilder dataBuilder = new StringBuilder();
            foreach (string key in tokenData.AllKeys)
            {
                string encodedKey = HttpUtility.UrlEncode(key);
                string encodedValue = HttpUtility.UrlEncode(tokenData[key]);

                if (dataBuilder.Length > 0)
                {
                    dataBuilder.Append('&');
                }
                dataBuilder.AppendFormat("{0}={1}", encodedKey, encodedValue);
            }
            string data = dataBuilder.ToString();

            HttpClient client = new HttpClient();
            HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://auth.mesabrook.com/token");
            requestMessage.Content = new StringContent(data, new MediaTypeHeaderValue(MediaTypeNames.Application.FormUrlEncoded));
            HttpResponseMessage? response = null;
            try
            {
                response = await client.SendAsync(requestMessage);
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Failed to acquire token:\r\n\r\n" + ex.ToString(), "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _closeFailAction();
                return;
            }

            string responseJsonString;
            using (StreamReader reader = new StreamReader(response.Content.ReadAsStream()))
            {
                responseJsonString = reader.ReadToEnd();
            }

            JsonNode? json = JsonNode.Parse(responseJsonString);
            if (json == null || json.GetValueKind() != JsonValueKind.Object)
            {
                MessageBox.Show("Response during token call was not of type json", "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _closeFailAction();
                return;
            }

            JsonObject responseObject = json.AsObject();
            if (!responseObject.TryGetPropertyValue("access_token", out JsonNode? accessTokenNode) || 
                !responseObject.TryGetPropertyValue("refresh_token", out JsonNode? refreshTokenNode))
            {
                MessageBox.Show("Could not find access token or refresh token during token call", "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _closeFailAction();
                return;
            }

            Config.AuthToken = accessTokenNode.GetValue<string>();
            Config.RefreshToken = refreshTokenNode.GetValue<string>();
            Config.Save();

            _closeSucceedAction();
        }
    }
}
