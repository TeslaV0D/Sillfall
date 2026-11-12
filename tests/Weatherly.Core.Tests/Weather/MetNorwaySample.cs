namespace Weatherly.Core.Tests.Weather;

/// <summary>A small hand-written answer in the format of MET Norway's compact forecast (no real data).</summary>
internal static class MetNorwaySample
{
    public static readonly DateTimeOffset FirstStep = new(2026, 11, 12, 6, 0, 0, TimeSpan.Zero);

    public const string Json = """
        {
          "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [13.4, 52.5, 40] },
          "properties": {
            "meta": { "updated_at": "2026-11-12T05:19:48Z", "units": { "air_temperature": "celsius" } },
            "timeseries": [
              {
                "time": "2026-11-12T06:00:00Z",
                "data": {
                  "instant": { "details": { "air_pressure_at_sea_level": 998.1, "air_temperature": 3.2, "cloud_area_fraction": 100.0,
                                            "relative_humidity": 96.8, "wind_from_direction": 243.9, "wind_speed": 1.7 } },
                  "next_1_hours": { "summary": { "symbol_code": "lightrain" }, "details": { "precipitation_amount": 0.4 } },
                  "next_6_hours": { "summary": { "symbol_code": "rain" }, "details": { "precipitation_amount": 3.0 } }
                }
              },
              {
                "time": "2026-11-12T07:00:00Z",
                "data": {
                  "instant": { "details": { "air_temperature": -1.5, "cloud_area_fraction": 40.0, "relative_humidity": 80.0,
                                            "wind_from_direction": 270.0, "wind_speed": 6.0 } },
                  "next_1_hours": { "summary": { "symbol_code": "snowshowers_day" }, "details": { "precipitation_amount": 1.2 } }
                }
              },
              {
                "time": "2026-11-12T12:00:00Z",
                "data": {
                  "instant": { "details": { "air_temperature": 5.0, "cloud_area_fraction": 10.0, "relative_humidity": 60.0,
                                            "wind_from_direction": 90.0, "wind_speed": 2.0 } },
                  "next_6_hours": { "summary": { "symbol_code": "clearsky_day" }, "details": { "precipitation_amount": 0.6 } }
                }
              },
              {
                "time": "2026-11-21T12:00:00Z",
                "data": { "instant": { "details": { "air_temperature": 5.3, "cloud_area_fraction": 100.0, "relative_humidity": 92.7,
                                                    "wind_from_direction": 264.7, "wind_speed": 4.3 } } }
              }
            ]
          }
        }
        """;
}
