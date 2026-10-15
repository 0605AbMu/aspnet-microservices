using System.Text.Json.Serialization;
using Microservices.Common.Models;

namespace Microservices.Common.Serialization;

/// <summary>
/// [REFERENCE IMPLEMENTATION / OPT-IN]:
/// Ushbu klass .NET C# Source Generator yordamida Reflection-siz (Zero-Allocation / Native AOT mosligi uchun)
/// JSON serilizatsiyasini sozlash namunasidir.
/// 
/// Qachon kerak:
/// 1. Loyihani Native AOT (Ahead-of-Time JIT-siz binary) sifatida kompilyatsiya qilganda.
/// 2. O'ta yuqori yuklamali Gateway yoki real-time oqimlarda (100k+ RPS) har bir mikrosekund muhim bo'lganda.
/// 
/// Qachon kerak EMAS:
/// Odatiy biznes mikroservislarida System.Text.Json ning standart reflection kesh mexanizmi
/// yetarli darajada tez (0.1-0.2ms) bo'lib, har bir DTO ni bu yerda qo'lda ro'yxatdan o'tkazish
/// dasturchiga ortiqcha yuk (overengineering) tug'diradi.
/// 
/// Foydalanish uchun (Program.cs da):
/// builder.Services.ConfigureHttpJsonOptions(options =>
/// {
///     options.SerializerOptions.TypeInfoResolverChain.Insert(0, SampleJsonSerializerContext.Default);
/// });
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ApiResponse<string>))]
[JsonSerializable(typeof(ApiResponse<long>))]
[JsonSerializable(typeof(ApiResponse<int>))]
[JsonSerializable(typeof(ApiResponse<bool>))]
[JsonSerializable(typeof(PagedRequest))]
public partial class SampleJsonSerializerContext : JsonSerializerContext
{
}
