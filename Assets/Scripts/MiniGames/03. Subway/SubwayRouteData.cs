using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// 한 개 지하철 노선의 역 순서를 보관한다.
internal sealed class SubwayRoute
{
    public readonly string Region;
    public readonly string LineName;
    public readonly List<(int Order, string Name)> OrderedStations = new();
    public readonly List<string> Stations = new();

    // 노선의 지역과 노선명을 저장한다.
    public SubwayRoute(string region, string lineName)
    {
        Region = region;
        LineName = lineName;
    }
}

// 지하철 CSV 해석과 노선 색상 조회를 담당한다.
internal static class SubwayRouteData
{
    // CSV 원본을 플레이 가능한 노선 목록으로 변환한다.
    public static List<SubwayRoute> Parse(byte[] csvBytes, int minimumStationCount)
    {
        string csv = GetEncoding().GetString(csvBytes);
        Dictionary<string, SubwayRoute> routes = new();

        foreach (string line in csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            string[] columns = line.Split(',');
            if (columns.Length < 6 || !int.TryParse(columns[4].Trim(), out int order))
            {
                continue;
            }

            string region = columns[1].Trim();
            string operatorName = columns[2].Trim();
            string lineName = columns[3].Trim();
            string stationName = RemoveParenthetical(columns[5]);
            bool isSuinBundang = operatorName == "코레일" && lineName == "수인분당";

            if ((!IsSupportedOperator(operatorName) && !isSuinBundang) || string.IsNullOrEmpty(stationName))
            {
                continue;
            }

            string key = $"{region}|{operatorName}|{lineName}";
            if (!routes.TryGetValue(key, out SubwayRoute route))
            {
                route = new SubwayRoute(region, lineName);
                routes.Add(key, route);
            }

            route.OrderedStations.Add((order, stationName));
        }

        foreach (SubwayRoute route in routes.Values)
        {
            route.Stations.AddRange(route.OrderedStations
                .OrderBy(station => station.Order)
                .Select(station => station.Name));
        }

        return routes.Values.Where(route => route.Stations.Count >= minimumStationCount).ToList();
    }

    // 노선 이름에 맞는 대표 색상을 반환한다.
    public static Color GetColor(string lineName)
    {
        Dictionary<string, string> colors = new()
        {
            ["1호선"] = "#004A85",
            ["2호선"] = "#00A23F",
            ["3호선"] = "#ED6C00",
            ["4호선"] = "#009BCE",
            ["5호선"] = "#794698",
            ["6호선"] = "#7C4932",
            ["7호선"] = "#6E7E31",
            ["8호선"] = "#D11D70",
            ["9호선"] = "#A49D87",
            ["경의중앙"] = "#6AC2B3",
            ["수인분당"] = "#ECA300",
            ["신분당"] = "#B81B30",
            ["인천1호선"] = "#B4C7E7",
            ["공항"] = "#0079AC",
            ["우이신설"] = "#BACC50",
            ["신림선"] = "#5E7DBB",
            ["의정부"] = "#F0831E",
            ["에버라인"] = "#44A436",
            ["인천2호선"] = "#F4A462",
            ["김포골드라인"] = "#957326",
            ["경춘"] = "#007A62",
            ["경강"] = "#0B318F",
            ["서해선"] = "#5EAC41",
            ["GTX-A"] = "#9A6292"
        };

        if (colors.TryGetValue(lineName, out string hex))
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }

        // 노선 이름이 미리 정의된 색상 목록에 없으면 해시값을 이용해 색상을 생성한다.
        int hash = lineName.Aggregate(17, (value, character) => value * 31 + character);
        return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.72f, 0.9f);
    }

    // CSV의 한글 인코딩을 선택한다.
    private static Encoding GetEncoding()
    {
        try
        {
            return Encoding.GetEncoding(949);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    // 역 이름 뒤의 괄호형 부가 명칭을 제거한다.
    private static string RemoveParenthetical(string stationName)
    {
        string name = stationName.Trim();
        int index = name.IndexOf('(');
        return index < 0 ? name : name.Substring(0, index).Trim();
    }

    // 미니게임에서 사용할 수도권 운영사인지 확인한다.
    private static bool IsSupportedOperator(string operatorName)
    {
        return operatorName == "서울교통공사"
            || operatorName == "서울시메트로9호선주식회사"
            || operatorName == "네오트랜스주식회사"
            || operatorName == "인천교통공사"
            || operatorName == "공항철도주식회사";
    }
}
