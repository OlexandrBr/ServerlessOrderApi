using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace OrderApi;

public class Function
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private static readonly string[] Statuses = { "New", "Processing", "Shipped", "Delivered", "Cancelled" };

    private readonly IAmazonS3 _s3 = new AmazonS3Client();
    private readonly IAmazonSQS _sqs = new AmazonSQSClient();
    private readonly string _bucket = Environment.GetEnvironmentVariable("BUCKET_NAME") ?? "";
    private readonly string _queueUrl = Environment.GetEnvironmentVariable("QUEUE_URL") ?? "";

    public async Task<APIGatewayHttpApiV2ProxyResponse> Handler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        try
        {
            if (request.RouteKey == "POST /orders")
            {
                return await CreateOrder(request);
            }

            if (request.RouteKey == "GET /orders/{id}")
            {
                return await GetOrder(request);
            }

            if (request.RouteKey == "PUT /orders/{id}/status")
            {
                return await UpdateStatus(request);
            }

            return Response(404, new { error = "Маршрут не знайдено" });
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex.ToString());
            return Response(500, new { error = "Помилка сервера" });
        }
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> CreateOrder(APIGatewayHttpApiV2ProxyRequest request)
    {
        var input = ReadBody<Order>(request);

        if (input == null || string.IsNullOrWhiteSpace(input.CustomerName) || string.IsNullOrWhiteSpace(input.Product) || input.Quantity <= 0)
        {
            return Response(400, new { error = "Потрібні customerName, product і quantity більше 0" });
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = input.CustomerName,
            Product = input.Product,
            Quantity = input.Quantity,
            Status = "New",
            CreatedAt = DateTime.UtcNow
        };

        await SaveOrder(order);

        await _sqs.SendMessageAsync(_queueUrl, JsonSerializer.Serialize(order, JsonOptions));

        return Response(201, order);
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> GetOrder(APIGatewayHttpApiV2ProxyRequest request)
    {
        var order = await LoadOrder(request);

        if (order == null)
        {
            return Response(404, new { error = "Замовлення не знайдено" });
        }

        return Response(200, order);
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> UpdateStatus(APIGatewayHttpApiV2ProxyRequest request)
    {
        var order = await LoadOrder(request);

        if (order == null)
        {
            return Response(404, new { error = "Замовлення не знайдено" });
        }

        var input = ReadBody<Order>(request);
        var status = Statuses.FirstOrDefault(s => s.Equals(input?.Status, StringComparison.OrdinalIgnoreCase));

        if (status == null)
        {
            return Response(400, new { error = "Статус має бути одним з: " + string.Join(", ", Statuses) });
        }

        order.Status = status;
        await SaveOrder(order);

        return Response(200, order);
    }

    private async Task SaveOrder(Order order)
    {
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = $"orders/{order.Id}.json",
            ContentBody = JsonSerializer.Serialize(order, JsonOptions),
            ContentType = "application/json"
        });
    }

    private async Task<Order?> LoadOrder(APIGatewayHttpApiV2ProxyRequest request)
    {
        string? id = null;
        request.PathParameters?.TryGetValue("id", out id);

        if (!Guid.TryParse(id, out var orderId))
        {
            return null;
        }

        try
        {
            using var response = await _s3.GetObjectAsync(_bucket, $"orders/{orderId}.json");
            using var reader = new StreamReader(response.ResponseStream);
            return JsonSerializer.Deserialize<Order>(await reader.ReadToEndAsync(), JsonOptions);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static T? ReadBody<T>(APIGatewayHttpApiV2ProxyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return default;
        }

        var body = request.IsBase64Encoded
            ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
            : request.Body;

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static APIGatewayHttpApiV2ProxyResponse Response(int statusCode, object body)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(body, JsonOptions),
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json; charset=utf-8"
            }
        };
    }
}
