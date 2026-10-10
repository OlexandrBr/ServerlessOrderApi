using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OrderClient;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.Unicode;

var config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("config.json"))!;

var http = new HttpClient
{
    BaseAddress = new Uri(config["ApiUrl"].TrimEnd('/') + "/")
};

var statuses = new[] { "New", "Processing", "Shipped", "Delivered", "Cancelled" };

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1 - Створити замовлення");
    Console.WriteLine("2 - Знайти замовлення за Id");
    Console.WriteLine("3 - Змінити статус");
    Console.WriteLine("0 - Вихід");
    Console.Write("Ваш вибір: ");

    var choice = Console.ReadLine();
    Console.WriteLine();

    if (choice == null || choice == "0")
    {
        break;
    }

    try
    {
        if (choice == "1")
        {
            await CreateOrder();
        }
        else if (choice == "2")
        {
            await FindOrder();
        }
        else if (choice == "3")
        {
            await ChangeStatus();
        }
        else
        {
            Console.WriteLine("Невідома команда.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Помилка: " + ex.Message);
    }
}

async Task CreateOrder()
{
    Console.Write("Ім'я клієнта: ");
    var customerName = Console.ReadLine() ?? "";

    Console.Write("Товар: ");
    var product = Console.ReadLine() ?? "";

    Console.Write("Кількість: ");
    int.TryParse(Console.ReadLine(), out var quantity);

    var response = await http.PostAsJsonAsync("orders", new { customerName, product, quantity });
    await ShowResult(response, "Замовлення створено:");
}

async Task FindOrder()
{
    Console.Write("Id замовлення: ");
    var id = Console.ReadLine()?.Trim();

    var response = await http.GetAsync($"orders/{id}");
    await ShowResult(response, "Замовлення:");
}

async Task ChangeStatus()
{
    Console.Write("Id замовлення: ");
    var id = Console.ReadLine()?.Trim();

    for (int i = 0; i < statuses.Length; i++)
    {
        Console.WriteLine($"{i + 1} - {statuses[i]}");
    }

    Console.Write("Новий статус: ");
    int.TryParse(Console.ReadLine(), out var number);

    if (number < 1 || number > statuses.Length)
    {
        Console.WriteLine("Невідомий статус.");
        return;
    }

    var response = await http.PutAsJsonAsync($"orders/{id}/status", new { status = statuses[number - 1] });
    await ShowResult(response, "Статус змінено:");
}

async Task ShowResult(HttpResponseMessage response, string title)
{
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        Console.WriteLine("Замовлення не знайдено.");
        return;
    }

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"Помилка {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return;
    }

    var order = await response.Content.ReadFromJsonAsync<Order>();

    Console.WriteLine(title);
    Console.WriteLine($"Id: {order!.Id}");
    Console.WriteLine($"Клієнт: {order.CustomerName}");
    Console.WriteLine($"Товар: {order.Product}");
    Console.WriteLine($"Кількість: {order.Quantity}");
    Console.WriteLine($"Статус: {order.Status}");
    Console.WriteLine($"Створено: {order.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
}
