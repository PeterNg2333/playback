// Nullable 練習：講師名稱同 slide 編號都可能「未有值」
string? lecturerName = null;
int? currentSlide = 12;

string lecturerLabel = lecturerName?.Trim() ?? "（未提供講師名稱）";
Console.WriteLine($"lecturer = {lecturerLabel}");
Console.WriteLine($"slide = {currentSlide ?? 0}");

lecturerName = "  Dr. Chan  ";
lecturerLabel = lecturerName?.Trim() ?? "（未提供講師名稱）";
Console.WriteLine($"lecturer (after set) = {lecturerLabel}");
