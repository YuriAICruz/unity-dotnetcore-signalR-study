﻿using System;
using System.Threading.Tasks;
using Graphene.ApiCommunication;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using UnityEngine;
using Zenject;
#if UNITY_EDITOR
using UnityEditor;

#endif

namespace Graphene.SignalR
{
    public class NetworkClientManager : IDisposable
    {
        public Action OnConnected, OnDisconnected;

        public Action<NetworkClient> OnClientConnected, OnClientDisconnected;
        
        private readonly int _timeout;
        private readonly Http _http;

        private readonly HubConnection _connection;
        private bool _isDisposed;
        private bool _connected;
        
        private string _userName;

        private NetworkClients _connections = new NetworkClients();
        

        public NetworkClientManager(string baseUrl, string socketPath, int timeout, Http http)
        {
            _http = http;
            _timeout = timeout;

            _connection = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}{socketPath}",
                    options => { options.Cookies = _http.GetCookieContainer(); })
                .Build();

            _connection.On<string, string>("OnConnected", ClientConnected);
            _connection.On<string, string>("OnDisconnected", ClientDisconnected);

            _connection.Closed += ReConnect;

            _isDisposed = false;
        }

        public void Dispose()
        {
            _connection.DisposeAsync();
            _isDisposed = true;
        }

        
        public async Task Connect(string userName)
        {
            _userName = userName;
            
            if (_isDisposed)
                return;
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif

            try
            {
                await _connection.StartAsync();

                OnConnectedToServer();
            }
            catch (System.Exception ex)
            {
                ReConnect(ex);
            }
        }
        
        private async Task ReConnect(Exception error)
        {
            Debug.LogError(error);

            if (_connected)
                OnDisconnectedToServer();
            
            await Task.Delay(_timeout);
            await Connect(_userName);
        }

        
        private void OnDisconnectedToServer()
        {
            _connected = false;
            OnDisconnected?.Invoke();
            Debug.Log("Disconnected");
        }

        private void OnConnectedToServer()
        {
            _connected = true;
            OnConnected?.Invoke();
            Debug.Log("Connection started");
        }

        
        private void ClientConnected(string userName, string id)
        {
            if (_userName == userName)
            {
                return;
            }

            var i = _connections.Add(userName, id);
            
            Debug.Log($"OnConnected: {userName} {userName == _userName}");
            
            OnClientConnected?.Invoke(_connections[i]);
            
        }
        
        private void ClientDisconnected(string userName, string id)
        {
            if (_userName == userName)
            {
                return;
            }
            
            var i = _connections.FindIndex(userName);
            
            if (i >= 0)
            {
                OnClientDisconnected?.Invoke(_connections[i]);
                _connections.RemoveAt(i);
            }
            else
            {
                Debug.LogError("Client not found on disconnection");
            }
        }

        #region Handlers

        public void RegisterHandler<T>(string handlerName, Guid guid, [NotNull] Action<T> response)
        {
            _connection.On<Guid, string>(handlerName, (id, json) =>
            {
                if(guid != id) return;
                
                try
                {
                    var res = JsonConvert.DeserializeObject<T>(json);

                    response(res);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError("Failed to deserialize message\n" + e);
                }
            });
        }

        public void RegisterHandler(string handlerName, Guid guid, [NotNull] Action<float> response)
        {
            _connection.On<Guid, float>(handlerName, (id, value) =>
            {
                if(guid != id) return;
                
                response(value);
            });
        }

        public void RegisterHandler(string handlerName, [NotNull] Action<int> response)
        {
            throw new NotImplementedException();
            _connection.On<string, string>(handlerName, (name, value) =>
            {
                if (int.TryParse(value, out var res))
                {
                    response(res);
                    return;
                }

                Debug.LogError("Failed to deserialize value");
            });
        }

        public void UnregisterHandler(string handlerName)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Sender

        public async void SendToAll<T>(string handler, Guid id, T updateTransform)
        {
            if(!_connected) return;
            
            try
            {
                await _connection.InvokeAsync("SendToAll", handler, id, JsonConvert.SerializeObject(updateTransform));
            }
            catch (System.Exception ex)
            {
                Debug.Log(ex.Message);
            }
        }

        #endregion
    }
}