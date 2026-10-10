# CodeForge — Kurumsal .NET için Roslyn Analizörleri, Code Fix'ler ve Source Generator'lar

[English](README.md) · **Türkçe**

Bir .NET kod tabanına derleme zamanı araçları ekleyen tek bir NuGet paketi (`analyzers/dotnet/cs`, çalışma zamanı bağımlılığı yok):
tekrarlı kodu ortadan kaldıran **artımlı (incremental) source generator'lar** ve async ile mimari hatalarını siz yazarken yakalayan **code fix'li analizörler**.

## Kurulum

```bash
dotnet add package CodeForge
```

Öznitelikler (`[StronglyTypedId]`, `[EnumExtensions]`, `[MapTo]`, `[assembly: ForbidDependency]`) kendi derlemenize `internal` tipler olarak eklenir; çalışma zamanında referans verilecek bir şey yoktur.
Roslyn 4.11 veya üstü bir SDK gerekir (.NET 8.0.4xx SDK / Visual Studio 17.11 ve sonrası).

## Source generator'lar

| Generator | Siz yazarsınız | Elde edersiniz |
|---|---|---|
| **Güçlü tipli kimlikler** | `[StronglyTypedId] public readonly partial struct OrderId;` (varsayılan `Guid`; `int`, `long`, `string` da olur) | Değer anlamlı bir struct: `New()`, `From()`, eşitlik ve sıralama operatörleri, `IParsable<T>`, `System.Text.Json` dönüştürücüsü (sözlük anahtarları dahil), model bağlama için `TypeConverter`. `OrderId` ile `CustomerId` artık birbirine karıştırılamaz |
| **Derleme zamanı mapper'lar** | `[MapTo(typeof(OrderDto))] public class Order { … }` | `order.ToOrderDto()`: yapıcılar ve initializer'lar otomatik seçilir; iç içe eşlenmiş tipler, `List`/dizi/`IEnumerable` koleksiyonlar, sayısal genişletme, eşleşen enum'lar, null yönetimi, `[MapIgnore]`, `[MapProperty("Diger")]`. Eşlenemeyen üye çalışma zamanı sürprizi değil **derleme hatasıdır** |
| **Enum uzantıları** | `[EnumExtensions] public enum Status { … }` | Yansıtma (reflection) olmadan `ToStringFast()`, `ToDescription()` (`[Description]`'dan), `TryParseFast()`, `IsDefinedFast()`, `GetNames()`, `GetValues()` |

```csharp
[StronglyTypedId] public readonly partial struct OrderId;

[EnumExtensions]
public enum OrderStatus { [Description("Ödeme bekleniyor")] Pending = 1, Paid = 2 }

[MapTo(typeof(OrderDto))]
public class Order { public OrderId Id { get; init; } public OrderStatus Status { get; init; } public List<OrderLine> Lines { get; init; } = new(); }

var dto = order.ToOrderDto();            // üretilen
var text = dto.Status.ToDescription();   // üretilen
```

Çalıştırılabilir örnek: [samples/CodeForge.Sample](samples/CodeForge.Sample).

### Mapper tanıları

| Kimlik | Önem | Anlamı |
|---|---|---|
| `CFG101` | Uyarı | Hedef üyenin kaynağı yok; varsayılan değerini korur. Kaynağı olmayan `required` üye veya yapıcı parametresi için kod hiç üretilmez |
| `CFG102` | Hata | İki üye eşlenemiyor (örneğin `string` → `int`, ya da üyeleri farklı enum'lar). Tipler arasına `[MapTo]` ekleyin veya üyeye `[MapIgnore]` koyun |
| `CFG103` | Uyarı | Null olabilen bir kaynak, null olamayan bir hedefi besliyor |
| `CFG104` | Hata | Hedefin erişilebilir bir yapıcısı yok, ya da kaynak iç içe bir tip |

`CFG001`–`CFG003`, geçersiz `[StronglyTypedId]` / `[EnumExtensions]` hedeflerini bildirir (partial struct değil, desteklenmeyen taban tip, iç içe tip).

## Analizörler ve code fix'ler

| Kimlik | Varsayılan | Kural | Code fix |
|---|---|---|---|
| `CF0002` | Uyarı | Bir `Task`'ı bloklamayın (`.Result`, `.Wait()`, `WaitAll/WaitAny`, `GetAwaiter().GetResult()`) | Kapsayan metot `async` ise `await` ile değiştirir |
| `CF0003` | Bilgi | `DateTime.Now/UtcNow/Today` ve `DateTimeOffset.Now/UtcNow` yerine `TimeProvider` tercih edin (yalnızca `TimeProvider`'ın olduğu yerde) | – |
| `CF0004` | Uyarı | Her çağrıda `new HttpClient()` yapmayın (statik alan, statik özellik ve statik yapıcılar serbest) | – |
| `CF0005` | Uyarı | `async void`'den kaçının (olay işleyicileri hariç) | Dönüş tipini `Task` yapar |
| `CF0006` | Uyarı | `async` metot veya lambda içinde `Thread.Sleep` | `await Task.Delay(...)` ile değiştirir |
| `CF0100` | Hata | **Mimari kural ihlali**: bir katmandaki tip, yasaklı katmandaki bir tipe başvuruyor | – |

Önem düzeyleri standart Roslyn düzeyleridir; `.editorconfig` ile değiştirilebilir: `dotnet_diagnostic.CF0003.severity = warning`.

`CF0001` (`CancellationToken` iletme) bilerek yok: .NET SDK bunun için zaten `CA2016` ile geliyor.

### Mimari kurallar

```csharp
[assembly: ForbidDependency("Shop.Domain", "Shop.Infrastructure")]
[assembly: ForbidDependency("Shop.Application", "Shop.Infrastructure")]
```

`Shop.Domain` (veya alt ad alanı) içindeki herhangi bir tip, `Shop.Infrastructure` tipine değindiğinde tam o başvuruda bildirilir; genel tip argümanları, iç içe tipler, tam nitelikli adlar ve statik üye erişimi dahil.
Ad alanı eşleşmesi tam bölüm bazındadır (`Shop.DomainExtras`, `Shop.Domain` sayılmaz).

## Nasıl test ediliyor

```bash
dotnet test --solution CodeForge.slnx     # Docker gerekmez
```

73 test:

- **Analizörler ve düzeltmeler:** işaretleme tabanlı beklentilerle (`{|CF0002:task.Result|}`) `Microsoft.CodeAnalysis.Testing`; bildirilen durumlar, sessiz kalması gereken yakın durumlar ve düzeltme çıktısı.
- **Generator'lar:** her generator gerçek bir `CSharpGeneratorDriver` içinde çalışır, çıktı derlemesi **üretilip yüklenir** ve üretilen kod çalışma zamanında sınanır (ayrıştırma, JSON gidiş-dönüşü, sözlük anahtarları, tip dönüştürücü, eşleme sonuçları).
  Üretilen kodun null analizi altında uyarısız derlenmesi de şart koşulur.
- **Artımlı davranış:** adım izleme açıkken özdeş derlemede tüm çıktılar yeniden kullanılır, ilgisiz bir dosyayı düzenlemek hiçbir şeyi yeniden üretmez, tek bir bildirimi değiştirmek yalnızca o çıktıyı yeniden üretir. Çıktı bayt bayt belirleyicidir (deterministik).
- **Paketleme:** paketlenen `.nupkg`, CI'da geçici bir proje tarafından tüketilir; kod üretmeli ve bir tanı yükseltmelidir.

## Tasarım notları

- Generator'lar `ForAttributeWithMetadataName` kullanır ve **değer eşitlikli modeller** (`EquatableArray<T>`, record'lar) üzerinde önbellekler; tanılar `Location` yerine düz veri olarak saklanır, böylece hiçbir şey sözdizimi ağaçlarını ya da sembolleri tutmaz. Bkz. [ADR 0001](docs/adr/0001-incremental-generators.md).
- Analizörler operasyon / sembol / sözdizimi eylemlerini eşzamanlı yürütme açık olarak kaydeder ve üretilmiş kodu atlar. Bkz. [ADR 0002](docs/adr/0002-analyzer-design.md).
- Mapper tasarım gereği açıktır: hiçbir şey sessizce tahmin edilmez. Bkz. [ADR 0003](docs/adr/0003-mapper-semantics.md).
- Mimari kurallar kodda, derleme düzeyi öznitelik olarak yaşar ve proje ile birlikte sürümlenir. Bkz. [ADR 0004](docs/adr/0004-architecture-rules.md).

## Bilinen sınırlar (dürüst liste)

- Mapper, Mapperly gibi kütüphanelerden bilerek daha küçüktür: kullanıcı tanımlı dönüştürücü yok, düzleştirme (`Customer.Address.City` → `CustomerCity`) yok, çok biçimli eşleme yok, `IQueryable` izdüşümü yok, sözlük ve değişmez koleksiyon yok.
- Güçlü tipli kimlikler yalnızca en üst düzey `partial struct` ve `Guid`, `int`, `long`, `string` destekler. EF Core değer dönüştürücüsü veya OpenAPI şema eşlemesi yok.
- `CF0002` niyet konusunda sözdizimseldir: zaten tamamlanmış bir görev üzerindeki `.Result`'ı da bildirir ve `Task.WhenAll(...).Result`'ı güvenli saymaz. Bloklama bilinçliyse (örneğin konsol `Main`) bastırın.
- `CF0100`, yönetilen ad alanlarının kodunda geçen adları denetler; `using` yönergelerini, proje/paket başvurularını, yansıtmayı veya dizeleri denetlemez. Bir koruma bariyeridir; derlemeleri yükleyen mimari testlerin yerini tutmaz.
- Analizörler Roslyn 4.11'i (`netstandard2.0`) hedefler ve .NET 10 SDK ile sınandı; eski SDK'lar denenmedi.
- Generator ve analizör **performansı çok büyük çözümlerde ölçülmedi**; yalnızca artımlı doğruluk sınanıyor.
- `CodeForge` NuGet paketi yerelde ve CI'da üretilip doğrulanıyor ama nuget.org'a **yayınlanmadı**.
