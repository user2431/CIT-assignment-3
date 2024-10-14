using System;
using System.Net;
using System.Net.Sockets;
using System.Text; 
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;

// Class representing a Category with two properties: cid and name
public class Category
{
    public int cid { get; set; }
    public string name { get; set; }
}

public class Server
{
    private TcpListener _listener;  // TcpListener to listen for incoming client connections
    private Dictionary<int, Category> _categories = new Dictionary<int, Category>  // In-memory store for categories
    {
        { 1, new Category { cid = 1, name = "Beverages" } },
        { 2, new Category { cid = 2, name = "Condiments" } },
        { 3, new Category { cid = 3, name = "Confections" } }
    };

    // Constructor to initialize the TcpListener
    public Server()
    {
        _listener = new TcpListener(IPAddress.Any, 5000);
    }

    // Start the server to listen for incoming connections
    public void Start()
    {
        _listener.Start();
        Console.WriteLine("Server started on port 5000...");

        // Accept incoming client connections and handle them in a thread pool
        while (true)
        {
            TcpClient client = _listener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(HandleClient, client);
        }
    }

    // Handle each client connection by reading, processing andresponding
    private void HandleClient(object obj)
    {
        TcpClient client = (TcpClient)obj;
        NetworkStream stream = client.GetStream();

        // Read the client's request
        byte[] buffer = new byte[client.ReceiveBufferSize];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        string requestString = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Process the request and get the response string
        string responseString = ProcessRequest(requestString);

        // Send the response back to the client
        byte[] responseBytes = Encoding.UTF8.GetBytes(responseString);
        stream.Write(responseBytes, 0, responseBytes.Length);

        // Close the connection
        client.Close();
    }

    // Process the client's request and generate the appropriate response
    private string ProcessRequest(string requestString)
    {
        try
        {
            // Deserialize the request into a dictionary of key-value pairs
            var request = JsonSerializer.Deserialize<Dictionary<string, object>>(requestString);

            // Validate that the request contains the necessary fields: method and date
            if (!request.ContainsKey("method") || !request.ContainsKey("date"))
            {
                return CreateErrorResponse(4, "missing method, missing date");
            }

            string method = request["method"].ToString();  
            string path = request.ContainsKey("path") ? request["path"].ToString() : ""; 

            // Validate the date field
            if (!IsValidUnixTimestamp(request["date"].ToString()))
            {
                return CreateErrorResponse(4, "illegal date");
            }

            long date = long.Parse(request["date"].ToString());

            // Process the request based on the method
            switch (method)
            {
                case "create":
                case "update":
                    return HandleCreateUpdate(method, path, request);
                case "echo":
                    return HandleEcho(request);
                case "read":
                case "delete":
                    return HandleRequestWithResource(method, path);
                default:
                    return CreateErrorResponse(4, "illegal method");  
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(6, "error: " + ex.Message);  
        }
    }

    // Check if the date is a valid Unix timestamp
    private bool IsValidUnixTimestamp(string dateString)
    {
        if (long.TryParse(dateString, out long unixTime))
        {
            DateTimeOffset dto = DateTimeOffset.FromUnixTimeSeconds(unixTime);
            return dto != DateTimeOffset.MinValue;
        }
        return false;
    }

    // Handle methods that require a valid resource path
    private string HandleRequestWithResource(string method, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateErrorResponse(4, "missing resource");  // Return "missing resource" if path is empty
        }

        // Validate that the path includes a valid integer ID
        string[] pathParts = path.Split('/');
        if (pathParts.Length == 4 && !int.TryParse(pathParts[3], out _))
        {
            return CreateErrorResponse(4, "bad request"); 
        }

        // Handle read and delete methods
        switch (method)
        {
            case "read":
                return HandleRead(path);
            case "delete":
                return HandleDelete(path);
            default:
                return CreateErrorResponse(4, "illegal method");
        }
    }

    // Handle create and update methods
    private string HandleCreateUpdate(string method, string path, Dictionary<string, object> request)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateErrorResponse(4, "missing resource");
        }

        // Ensure the request contains a body for create/update methods
        if (!request.ContainsKey("body"))
        {
            return CreateErrorResponse(4, "missing body");
        }

        try
        {
            // Deserialize the body of the request
            var body = JsonSerializer.Deserialize<Dictionary<string, object>>(request["body"].ToString());

            switch (method)
            {
                case "create":
                    return HandleCreate(body, path);
                case "update":
                    return HandleUpdate(body, path);
                default:
                    return CreateErrorResponse(4, "illegal method");
            }
        }
        catch (JsonException)
        {
            return CreateErrorResponse(4, "illegal body");  // Catch any JSON parsing errors in the body
        }
    }

    // Handle the creation of a new category
    private string HandleCreate(Dictionary<string, object> body, string path)
    {
        if (path != "/api/categories")
        {
            return CreateErrorResponse(4, "bad request");  
        }

        // Create a new category and add it to the in-memory store
        var newCategory = new Category { cid = _categories.Count + 1, name = body["name"].ToString() };
        _categories[newCategory.cid] = newCategory;

        // Return a success response with the created category
        return CreateSuccessResponse(2, JsonSerializer.Serialize(newCategory));
    }

    // Handle reading categories or a specific category
    private string HandleRead(string path)
    {
        if (path == "/api/categories")
        {
            return CreateSuccessResponse(1, JsonSerializer.Serialize(_categories.Values));  // Return all categories
        }

        // Handle reading a specific category by ID
        if (path.StartsWith("/api/categories/"))
        {
            int id = int.Parse(path.Split('/')[3]);
            if (_categories.ContainsKey(id))
            {
                return CreateSuccessResponse(1, JsonSerializer.Serialize(_categories[id]));
            }

            return CreateErrorResponse(5, "not found");  
        }

        return CreateErrorResponse(4, "bad request");  
    }

    // Handle updating an existing category
    private string HandleUpdate(Dictionary<string, object> body, string path)
    {
        if (!path.StartsWith("/api/categories/"))
        {
            return CreateErrorResponse(4, "bad request");
        }

        int id = int.Parse(path.Split('/')[3]);
        if (_categories.ContainsKey(id))
        {
            _categories[id].name = body["name"].ToString();
            return CreateSuccessResponse(3, "Updated");  // Return success if the category is updated
        }

        return CreateErrorResponse(5, "not found");  // Return "not found" if category ID does not exist
    }

    // Handle deleting a category by ID
    private string HandleDelete(string path)
    {
        if (path.StartsWith("/api/categories/"))
        {
            int id = int.Parse(path.Split('/')[3]);
            if (_categories.ContainsKey(id))
            {
                _categories.Remove(id);
                return CreateSuccessResponse(1, "Ok"); 
            }

            return CreateErrorResponse(5, "not found");  
        }

        return CreateErrorResponse(4, "bad request");  
    }

    // Handle the echo method, which returns the body sent in the request
    private string HandleEcho(Dictionary<string, object> request)
    {
        if (!request.ContainsKey("body"))
        {
            return CreateErrorResponse(4, "missing body");
        }

        string body = request["body"].ToString();
        return CreateSuccessResponse(1, body);
    }

    // Helper function to create error responses with a status code and reason
    private string CreateErrorResponse(int statusCode, string reason)
    {
        return JsonSerializer.Serialize(new { status = $"{statusCode} {reason}", body = (string)null });
    }

    // Helper function to create success responses with a status code and body
    private string CreateSuccessResponse(int statusCode, string body)
    {
        string reasonPhrase = statusCode switch
        {
            1 => "Ok",      
            2 => "Created",  
            3 => "Updated",  
            _ => ""
        };
        return JsonSerializer.Serialize(new { status = $"{statusCode} {reasonPhrase}", body });
    }

    // Entry point of the program to start the server
    public static void Main(string[] args)
    {
        Server server = new Server();
        server.Start();
    }
}

