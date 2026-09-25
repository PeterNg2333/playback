using System.Text;

static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

Check(Providers.ParseAsr(Json("{\"raw\":\"exact [unclear]\",\"text\":\"model revision\"}")) == "exact [unclear]", "ASR original must win over a processed text field");
Check(Providers.ParseAsr(Json("{\"raw\":\"\"}")) == "", "Silent audio must stay uncertain, not become invented text");
try { Providers.ParseAsr(Json("{\"status\":\"ok\"}")); throw new Exception("Unknown ASR schema was accepted"); }
catch (InvalidOperationException) { }

var grounded = Providers.ParseGrounding(Json("""
{"steps":[{"type":"model_output","content":[{"type":"text","text":"Acoustic features summarize a waveform.","annotations":[{"type":"url_citation","url":"https://example.org/source","title":"Example source","start_index":0,"end_index":17},{"type":"url_citation","url":"javascript:alert(1)","title":"Unsafe","start_index":0,"end_index":17}]}]}]}
"""));
Check(grounded.Answer == "Acoustic features summarize a waveform.", "Grounded answer text was lost");
Check(grounded.Evidence.Count == 1, "Unsafe or missing citation was accepted");
Check(grounded.Evidence[0].StartIndex == 0 && grounded.Evidence[0].EndIndex == 17, "Citation span changed");
try { Providers.ParseGrounding(Json("{\"steps\":[]}")); throw new Exception("Empty grounded response was accepted"); }
catch (InvalidOperationException) { }
Console.WriteLine("Protocol checks passed: ASR original, silence, provider failures, grounded citation mapping");
