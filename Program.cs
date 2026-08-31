var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

string homePage = """
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <title>Weather Dashboard</title>

    <style>
        body {
            font-family: Arial, sans-serif;
            background: #eaf3fb;
            display: flex;
            justify-content: center;
            align-items: center;
            min-height: 100vh;
            margin: 0;
        }

        .card {
            background: white;
            padding: 30px 40px;
            border-radius: 12px;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
            text-align: center;
            width: 320px;
        }

        h1 {
            font-size: 24px;
            color: #333;
            margin-top: 0;
        }

        .temperature {
            font-size: 36px;
            font-weight: bold;
            color: #1e6fbf;
            margin: 15px 0;
        }

        .details {
            color: #555;
            font-size: 14px;
            line-height: 1.8;
        }

        .note {
            color: #777;
            font-size: 12px;
            margin-top: 20px;
        }
    </style>
</head>

<body>

    <div class="card">

        <h1>Weather Dashboard</h1>

        <div class="temperature">25°C</div>

        <p>Hello Pramod, today is sunny in Perth.</p>

        <div class="details">
            Minimum Temperature: 15°C<br>
            Maximum Temperature: 25°C<br>
            Humidity: 40%<br>
            Rainfall: 0 mm
        </div>

    </div>

</body>
</html>
""";

app.MapGet("/", () => Results.Content(homePage, "text/html"));

app.Run();