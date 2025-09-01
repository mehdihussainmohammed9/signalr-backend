using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Collections.Concurrent;

namespace backend.Hubs
{
    public class GridHub : Hub
    {
        private static readonly ConcurrentDictionary<string, UserInfo> _connectedUsers = new();
        private static readonly ConcurrentDictionary<string, CellSelection> _gridSelections = new();
        private static readonly string[] _userColors = { "red", "blue", "green", "purple", "orange", "pink", "cyan", "yellow", "indigo", "teal" };

        public class UserInfo
        {
            public string ConnectionId { get; set; } = "";
            public string UserName { get; set; } = "";
            public string Color { get; set; } = "";
            public DateTime ConnectedAt { get; set; }
        }

        public class CellSelection
        {
            public string CellId { get; set; } = "";
            public string UserId { get; set; } = "";
            public string UserName { get; set; } = "";
            public string Color { get; set; } = "";
            public DateTime SelectedAt { get; set; }
        }

        public override async Task OnConnectedAsync()
        {
            // Generate a simple user name and assign color
            string userName = $"User{_connectedUsers.Count + 1}";
            string userColor = _userColors[_connectedUsers.Count % _userColors.Length];
            
            var userInfo = new UserInfo
            {
                ConnectionId = Context.ConnectionId,
                UserName = userName,
                Color = userColor,
                ConnectedAt = DateTime.Now
            };
            
            _connectedUsers[Context.ConnectionId] = userInfo;
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {userName} connected with color {userColor}");
            
            // Send user their info
            await Clients.Caller.SendAsync("UserInfo", userInfo);
            
            // Send current grid state to new user
            await Clients.Caller.SendAsync("GridState", _gridSelections.Values.ToArray());
            
            // Notify others about new user
            await Clients.Others.SendAsync("UserJoined", userInfo);
            
            // Send updated user list to everyone
            await Clients.All.SendAsync("ConnectedUsers", _connectedUsers.Values.ToArray());
            
            await base.OnConnectedAsync();
        }

        public async Task SelectCell(string cellId)
        {
            if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var user))
                return;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {user.UserName} selected cell {cellId}");

            // Remove user's previous selection
            var previousSelection = _gridSelections.Values.FirstOrDefault(s => s.UserId == Context.ConnectionId);
            if (previousSelection != null)
            {
                _gridSelections.TryRemove(previousSelection.CellId, out _);
                await Clients.All.SendAsync("CellDeselected", previousSelection.CellId);
            }

            // Add new selection
            var selection = new CellSelection
            {
                CellId = cellId,
                UserId = Context.ConnectionId,
                UserName = user.UserName,
                Color = user.Color,
                SelectedAt = DateTime.Now
            };

            _gridSelections[cellId] = selection;
            
            // Broadcast to all users
            await Clients.All.SendAsync("CellSelected", selection);
        }

        public async Task DeselectCell()
        {
            if (!_connectedUsers.TryGetValue(Context.ConnectionId, out var user))
                return;

            // Remove user's current selection
            var currentSelection = _gridSelections.Values.FirstOrDefault(s => s.UserId == Context.ConnectionId);
            if (currentSelection != null)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {user.UserName} deselected cell {currentSelection.CellId}");
                
                _gridSelections.TryRemove(currentSelection.CellId, out _);
                await Clients.All.SendAsync("CellDeselected", currentSelection.CellId);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectedUsers.TryRemove(Context.ConnectionId, out var user))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {user.UserName} disconnected");

                // Remove user's selection
                var userSelection = _gridSelections.Values.FirstOrDefault(s => s.UserId == Context.ConnectionId);
                if (userSelection != null)
                {
                    _gridSelections.TryRemove(userSelection.CellId, out _);
                    await Clients.All.SendAsync("CellDeselected", userSelection.CellId);
                }

                // Notify others
                await Clients.All.SendAsync("UserLeft", user);
                await Clients.All.SendAsync("ConnectedUsers", _connectedUsers.Values.ToArray());
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}