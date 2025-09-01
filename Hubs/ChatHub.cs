using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace backend.Hubs
{
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _backgroundTasks = new();
        private static readonly ConcurrentDictionary<string, DateTime> _connectedUsers = new();
        private static readonly ConcurrentDictionary<string, string> _gridCellActivity = new(); // cellId -> connectionId
        private static readonly ConcurrentDictionary<string, string> _userColors = new(); // connectionId -> color
        public async Task SendData(string data)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received from frontend: {data}");
            
            // Send modified data back to frontend with backend signature
            string backendResponse = $"🔥 BACKEND PROCESSED: '{data}' | Server Time: {DateTime.Now:HH:mm:ss} | Status: SUCCESS ✅";
            await Clients.Caller.SendAsync("ReceiveMessage", backendResponse);
        }

        public async Task GridCellSelected(string cellId)
        {
            // Track which user is active on which grid cell
            _gridCellActivity[cellId] = Context.ConnectionId;
            var userColor = _userColors.GetValueOrDefault(Context.ConnectionId, "blue");
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] User {Context.ConnectionId.Substring(0, 6)}... selected grid cell: {cellId} (color: {userColor})");
            
            // Broadcast the cell selection to all clients including the sender
            await Clients.All.SendAsync("GridCellActivity", cellId, Context.ConnectionId, userColor);
            
            // Also send confirmation back to sender
            await Clients.Caller.SendAsync("GridSelectionConfirmed", cellId, Context.ConnectionId, userColor);
        }

        public async Task GridCellDeselected(string cellId)
        {
            // Only remove if this user was the one who selected it
            if (_gridCellActivity.TryGetValue(cellId, out var activeUser) && activeUser == Context.ConnectionId)
            {
                _gridCellActivity.TryRemove(cellId, out _);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] User {Context.ConnectionId.Substring(0, 6)}... deselected grid cell: {cellId}");
                
                // Broadcast the cell deselection to all clients
                await Clients.All.SendAsync("GridCellDeselected", cellId);
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] User {Context.ConnectionId.Substring(0, 6)}... tried to deselect cell {cellId} but it's not theirs or doesn't exist");
            }
        }

        public async Task SendMessage(string user, string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Message from {user}: {message}");
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            await Clients.Group(groupName).SendAsync("UserJoined", $"{Context.ConnectionId} joined {groupName}");
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            await Clients.Group(groupName).SendAsync("UserLeft", $"{Context.ConnectionId} left {groupName}");
        }

        public async Task SendToGroup(string groupName, string user, string message)
        {
            await Clients.Group(groupName).SendAsync("ReceiveMessage", user, message);
        }

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected: {Context.ConnectionId}");
            
            // Add user to connected users list
            _connectedUsers[Context.ConnectionId] = DateTime.Now;
            
            // Assign a unique color to the user
            string[] colors = { "red", "blue", "green", "purple", "orange", "pink", "cyan", "yellow", "indigo", "teal" };
            string userColor = colors[_connectedUsers.Count % colors.Length];
            _userColors[Context.ConnectionId] = userColor;
            
            // Send welcome message immediately after connection with connection ID
            await Clients.Caller.SendAsync("ReceiveMessage", "🎉 Welcome! Connection established successfully!");
            
            // Send test event to verify connection
            await Clients.Caller.SendAsync("TestEvent", "Grid events should work now!");
            
            // Send the client their own connection ID and color
            await Clients.Caller.SendAsync("YourConnectionId", Context.ConnectionId);
            await Clients.Caller.SendAsync("YourUserColor", userColor);
            
            // Send current user list to the new client
            await Clients.Caller.SendAsync("AllConnectedUsers", _connectedUsers.Keys.ToArray());
            
            // Send current grid cell activity to new user
            var gridActivity = _gridCellActivity.ToDictionary(
                kvp => kvp.Key, 
                kvp => new { ConnectionId = kvp.Value, Color = _userColors.GetValueOrDefault(kvp.Value, "blue") }
            );
            await Clients.Caller.SendAsync("CurrentGridActivity", gridActivity);
            
            // Start sending periodic data automatically with cancellation support
            var cancellationTokenSource = new CancellationTokenSource();
            _backgroundTasks[Context.ConnectionId] = cancellationTokenSource;
            
            var hubContext = Context.GetHttpContext()?.RequestServices.GetService<IHubContext<ChatHub>>();
            if (hubContext != null)
            {
                _ = Task.Run(async () => await SendPeriodicData(Context.ConnectionId, cancellationTokenSource.Token, hubContext));
            }
            
            // Notify all clients about the new connection
            await Clients.All.SendAsync("UserConnected", Context.ConnectionId);
            
            // Send updated user list to all clients
            await Clients.All.SendAsync("AllConnectedUsers", _connectedUsers.Keys.ToArray());
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total connected users: {_connectedUsers.Count}");
            await base.OnConnectedAsync();
        }
        
        private static async Task SendPeriodicData(string connectionId, CancellationToken cancellationToken, IHubContext<ChatHub> hubContext)
        {
            int counter = 1;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Send automatic data every 5 seconds
                    string autoMessage = $"📡 AUTO MESSAGE #{counter} from Backend | Time: {DateTime.Now:HH:mm:ss} | Random: {new Random().Next(1000, 9999)}";
                    
                    await hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", autoMessage, cancellationToken);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Auto-sent to {connectionId}: Message #{counter}");
                    
                    counter++;
                    await Task.Delay(5000, cancellationToken); // Wait 5 seconds
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Auto messaging stopped for {connectionId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error sending auto data: {ex.Message}");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client disconnected: {Context.ConnectionId}");
            
            // Remove user from connected users list
            _connectedUsers.TryRemove(Context.ConnectionId, out _);
            
            // Remove user's color assignment
            _userColors.TryRemove(Context.ConnectionId, out _);
            
            // Clear all grid cell activities for this user
            var userCells = _gridCellActivity.Where(kvp => kvp.Value == Context.ConnectionId).ToList();
            foreach (var cell in userCells)
            {
                _gridCellActivity.TryRemove(cell.Key, out _);
                await Clients.All.SendAsync("GridCellDeselected", cell.Key);
            }
            
            // Stop the background task for this connection
            if (_backgroundTasks.TryRemove(Context.ConnectionId, out var cancellationTokenSource))
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
            
            // Notify all clients about the disconnection
            await Clients.All.SendAsync("UserDisconnected", Context.ConnectionId);
            
            // Send updated user list to all remaining clients
            await Clients.All.SendAsync("AllConnectedUsers", _connectedUsers.Keys.ToArray());
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total connected users: {_connectedUsers.Count}");
            await base.OnDisconnectedAsync(exception);
        }
    }
}