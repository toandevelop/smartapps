using SmartApps.Api.Models;
using SmartApps.Api.Services;
using Xunit;

namespace SmartApps.Api.Tests;

public class AiServiceTests
{
    [Fact]
    public void MaskApiKey_HidesMiddleCharactersAndHandlesShortOrEmpty()
    {
        Assert.Null(AiService.MaskApiKey(null));
        Assert.Null(AiService.MaskApiKey("   "));
        Assert.Equal("••••••••", AiService.MaskApiKey("short"));
        Assert.Equal("sk-p••••9012", AiService.MaskApiKey("sk-proj-abcdef1234569012"));
    }

    [Fact]
    public void IsProviderConfigured_RequiresKeyForCloudProvidersButAllowsLocalOpenAiCompatible()
    {
        Assert.False(AiService.IsProviderConfigured("GEMINI", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash", null));
        Assert.True(AiService.IsProviderConfigured("GEMINI", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash", "AIzaSy-123"));
        Assert.True(AiService.IsProviderConfigured("OPENAI_COMPATIBLE", "http://localhost:11434/v1", "qwen2.5:7b", null));
        Assert.False(AiService.IsProviderConfigured("OPENAI_COMPATIBLE", "", "qwen2.5:7b", null));
    }

    [Fact]
    public void ExtractTextFromResponses_ParsesOpenAiGeminiAndAnthropicPayloads()
    {
        var openAiJson = """
            {
              "choices": [{"message": {"content": "Xin chào từ GPT"}}],
              "usage": {"prompt_tokens": 12, "completion_tokens": 6}
            }
            """;
        var (oText, oPt, oCt) = AiService.ExtractTextFromOpenAiResponse(openAiJson);
        Assert.Equal("Xin chào từ GPT", oText);
        Assert.Equal(12, oPt);
        Assert.Equal(6, oCt);

        var geminiJson = """
            {
              "candidates": [{"content": {"parts": [{"text": "Xin chào "}, {"text": "từ Gemini"}]}}],
              "usageMetadata": {"promptTokenCount": 15, "candidatesTokenCount": 8}
            }
            """;
        var (gText, gPt, gCt) = AiService.ExtractTextFromGeminiResponse(geminiJson);
        Assert.Equal("Xin chào từ Gemini", gText);
        Assert.Equal(15, gPt);
        Assert.Equal(8, gCt);

        var claudeJson = """
            {
              "content": [{"type": "text", "text": "Xin chào từ Claude"}],
              "usage": {"input_tokens": 10, "output_tokens": 5}
            }
            """;
        var (aText, aPt, aCt) = AiService.ExtractTextFromAnthropicResponse(claudeJson);
        Assert.Equal("Xin chào từ Claude", aText);
        Assert.Equal(10, aPt);
        Assert.Equal(5, aCt);
    }

    [Fact]
    public void TryParseAiGeneratedAppSpec_ParsesMarkdownWrappedJsonAndSanitizesTypes()
    {
        var raw = """
            ```json
            {
              "code": "crm_lead!",
              "name": "Quản lý Cơ hội Kinh doanh",
              "department": "Kinh doanh",
              "icon": "People",
              "color": "#1a73e8",
              "bgColor": "#e8f0fe",
              "tables": [
                {
                  "tableName": "crm co hoi",
                  "displayName": "Danh sách Cơ hội",
                  "approvalEnabled": true,
                  "publicFormEnabled": false,
                  "columns": [
                    {"columnCode": "ma_co_hoi", "columnName": "Mã cơ hội", "dataType": "AUTO_NUMBER", "isRequired": true, "isIdentityKey": true, "defaultValue": "OPP-{YYYY}-"},
                    {"columnCode": "ten_kh", "columnName": "Khách hàng", "dataType": "STRING", "isRequired": true},
                    {"columnCode": "gia_tri", "columnName": "Giá trị dự kiến", "dataType": "CURRENCY"},
                    {"columnCode": "loai_la", "columnName": "UnknownType", "dataType": "INVALID_TYPE"}
                  ]
                }
              ]
            }
            ```
            """;

        var ok = MetadataService.TryParseAiGeneratedAppSpec(raw, new AiGenerateAppRequest
        {
            Prompt = "Tạo app CRM",
            AppCode = "SALES_AI"
        }, out var spec);

        Assert.True(ok);
        Assert.NotNull(spec);
        Assert.Equal("SALES_AI", spec!.Code);
        Assert.Equal("Kinh doanh", spec.Department);
        Assert.Single(spec.Tables);
        var tbl = spec.Tables[0];
        Assert.Equal("crm_co_hoi", tbl.TableName);
        Assert.True(tbl.Approval);
        Assert.Equal(4, tbl.Cols.Count);
        Assert.Equal("AUTO_NUMBER", tbl.Cols[0].DataType);
        Assert.Equal("STRING", tbl.Cols[3].DataType); // sanitized invalid type to STRING
    }
}
