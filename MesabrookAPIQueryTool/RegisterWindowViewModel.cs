using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using System.Windows;
using System.Windows.Input;

namespace MesabrookAPIQueryTool
{
    public class RegisterWindowViewModel : ObservableObject
    {
        private Action _closeFormAction;
        public RegisterWindowViewModel(Action closeFormAction)
        {
            _closeFormAction = closeFormAction;
        }

        private string? _clientID;
        public string? ClientID
        {
            get => _clientID;
            set => SetProperty(ref _clientID, value);
        }

        private bool _isEnteringClientID = true;
        public bool IsEnteringClientID
        {
            get => _isEnteringClientID;
            set
            {
                SetProperty(ref _isEnteringClientID, value);
                if (value)
                {
                    IsRegisteringOnline = false;
                    if (_registerTokenSource != null)
                    {
                        _registerTokenSource.Cancel();
                    }
                }
            }
        }

        private bool _isRegisteringOnline;
        public bool IsRegisteringOnline
        {
            get => _isRegisteringOnline;
            set
            {
                SetProperty(ref _isRegisteringOnline, value);
                if (value)
                {
                    IsEnteringClientID = false;

                    RegistrationURL = $"https://auth.mesabrook.com/register?clientName=Mesabrook%20API%20Query%20Tool&redirectionUri={WebUtility.UrlEncode(Config.LocalRedirectURI)}&postToRedirectUri=true";
                    ListenForRegister();
                }
                else
                {
                    RegistrationURL = "about:blank";
                }
            }
        }

        private string _registrationURL = "about:blank";
        public string RegistrationURL
        {
            get => _registrationURL;
            set => SetProperty(ref _registrationURL, value);
        }

        private CancellationTokenSource? _registerTokenSource;
        private async void ListenForRegister()
        {
            if (_registerTokenSource != null && !_registerTokenSource.IsCancellationRequested)
            {
                _registerTokenSource.Cancel();
            }

            _registerTokenSource = new CancellationTokenSource();

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add($"{Config.LocalRedirectURI}/");
            try
            {
                listener.Start();
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Could not start local HTTP listener at {Config.LocalRedirectURI}.\r\n\r\n{ex.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string? _clientID = null;
            while(true)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(_registerTokenSource.Token);
                    string body;
                    using(StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? System.Text.Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }

                    NameValueCollection formValues = HttpUtility.ParseQueryString(body);
                    if (!formValues.AllKeys.Contains("client_id"))
                    {
                        continue;
                    }

                    _clientID = formValues["client_id"];
                    break;
                }
                catch(TaskCanceledException)
                {
                    break;
                }
                finally
                {
                    context?.Response.Close();
                }
            }
            listener.Stop();

            if (_clientID != null)
            {
                SaveClientID(_clientID);
                _closeFormAction();
            }
        }

        public ICommand SaveClientIDCommand => new RelayCommand(SaveManualClientID);

        private void SaveManualClientID()
        {
            if (IsEnteringClientID)
            {
                if (string.IsNullOrEmpty(ClientID))
                {
                    MessageBox.Show("Client ID cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SaveClientID(ClientID);
                _closeFormAction();
            }
        }

        private void SaveClientID(string clientID)
        {
            Config.ClientID = clientID;
            Config.Save();
        }

        public ICommand WindowClosingCommand => new RelayCommand(WindowClosing);

        private void WindowClosing()
        {
            if (_registerTokenSource != null)
            {
                _registerTokenSource.Cancel();
            }
        }
    }
}
