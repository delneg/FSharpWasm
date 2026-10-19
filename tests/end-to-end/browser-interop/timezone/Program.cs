using System;

// Prints the zone the host selected and one fixed instant in it.
var zone = TimeZoneInfo.Local;
var instant = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
var local = TimeZoneInfo.ConvertTimeFromUtc(instant, zone);
Console.WriteLine($"{zone.Id} {local:yyyy-MM-dd HH:mm}");
