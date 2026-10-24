using LuMail.Core.Catalog;
using LuMail.Core.Classification;
using LuMail.Core.Models;
using LuMail.Core.Storage;

namespace LuMail.Core.Tests;

public class CategoryClassifierTests
{
    private static readonly CategoryCatalog Catalog = CategoryCatalog.LoadEmbedded();

    private static MessageCategory Classify(string from, string subject = "Hallo",
        Dictionary<string, string>? headers = null, MessageCategory? server = null, ISenderRuleLookup? rules = null) =>
        new CategoryClassifier(Catalog, rules).Classify(new ClassificationInput(from, subject, headers, server));

    private static Dictionary<string, string> H(string name, string value) =>
        new(StringComparer.OrdinalIgnoreCase) { [name] = value };

    [Fact]
    public void Catalog_HasFiveCategoriesInOrder()
    {
        Assert.Equal(new[] { "primary", "promotions", "social", "updates", "forums" }, Catalog.Categories.Select(c => c.Id));
        Assert.Equal("Werbung", Catalog.DisplayName(MessageCategory.Promotions));
        Assert.Equal("category:promotions", Catalog.GmailQuery(MessageCategory.Promotions));
        Assert.Equal(8, Catalog.Algorithm.Count(a => char.IsDigit(a[0])));
    }

    [Fact]
    public void Step1_UserRuleWinsOverEverything()
    {
        var rules = SenderRuleStore.InMemory();
        rules.Set("friend@facebook.com", MessageCategory.Primary);
        // Ohne Regel wäre es social; mit Serverkategorie wäre es updates – die Regel gewinnt immer.
        Assert.Equal(MessageCategory.Primary, Classify("friend@facebook.com", rules: rules));
        Assert.Equal(MessageCategory.Primary, Classify("Friend@Facebook.com", server: MessageCategory.Updates, rules: rules));
    }

    [Fact]
    public void Step1_DomainRuleAppliesToSubdomains_AddressRuleBeatsDomainRule()
    {
        var rules = SenderRuleStore.InMemory();
        rules.Set("shop.de", MessageCategory.Promotions);
        rules.Set("rechnung@shop.de", MessageCategory.Updates);
        Assert.Equal(MessageCategory.Promotions, Classify("info@mail.shop.de", rules: rules));
        Assert.Equal(MessageCategory.Updates, Classify("rechnung@shop.de", rules: rules));
        Assert.Equal(MessageCategory.Primary, Classify("someone@othershop.de", rules: rules));
    }

    [Fact]
    public void Step2_GmailServerCategoryWinsOverLocalRules()
    {
        Assert.Equal(MessageCategory.Primary, Classify("noreply@facebookmail.com", "Neue Freundschaftsanfrage", server: MessageCategory.Primary));
        Assert.Equal(MessageCategory.Forums, Classify("x@y.de", server: MessageCategory.Forums));
    }

    [Theory]
    [InlineData("notification@facebookmail.com")]
    [InlineData("info@linkedin.com")]
    [InlineData("noreply@mail.instagram.com")] // Subdomain
    [InlineData("hi@my.mail.ru")]
    [InlineData("x@SUB.Reddit.COM")]
    public void Step3_SocialDomains(string from) => Assert.Equal(MessageCategory.Social, Classify(from));

    [Theory]
    [InlineData("someone@notfacebook.com")]
    [InlineData("someone@facebook.com.evil.org")]
    [InlineData("someone@mail.ru")] // nur my.mail.ru ist social
    public void Step3_NoFalseDomainMatches(string from) => Assert.NotEqual(MessageCategory.Social, Classify(from));

    [Fact]
    public void Step3_SocialBeatsUpdatesKeyword() =>
        Assert.Equal(MessageCategory.Social, Classify("security@facebookmail.com", "Neue Anmeldung bei Facebook"));

    [Fact]
    public void Step4_ForumsByDomainOrListPostHeader()
    {
        Assert.Equal(MessageCategory.Forums, Classify("group@googlegroups.com"));
        Assert.Equal(MessageCategory.Forums, Classify("x@list.groups.io"));
        // List-Post schlägt sogar Betreff-Stichwort "invoice" (Schritt 4 vor 5).
        Assert.Equal(MessageCategory.Forums, Classify("dev@example.org", "Invoice discussion", H("List-Post", "<mailto:dev@example.org>")));
    }

    [Fact]
    public void Step4_EmptyHeaderCountsAsAbsent() =>
        Assert.Equal(MessageCategory.Primary, Classify("a@b.de", "Hallo", H("List-Post", "  ")));

    [Theory]
    [InlineData("Ihre Rechnung für März")]
    [InlineData("Your ORDER has shipped")]
    [InlineData("Ваш заказ отправлен")]
    [InlineData("Код подтверждения: 1234")]
    [InlineData("Счёт на оплату")]
    public void Step5_UpdatesSubjectKeywords(string subject) =>
        Assert.Equal(MessageCategory.Updates, Classify("max@example.org", subject));

    [Fact]
    public void Step5_UpdatesKeywordBeatsPromotionsHeader() =>
        Assert.Equal(MessageCategory.Updates, Classify("shop@example.org", "Deine Bestellung", H("List-Unsubscribe", "<https://x>")));

    [Fact]
    public void Step6_PromotionsByHeader() =>
        Assert.Equal(MessageCategory.Promotions, Classify("anna@example.org", "Hallo", H("list-unsubscribe", "<mailto:u@x>")));

    [Theory]
    [InlineData("bulk")]
    [InlineData("List")]
    [InlineData(" junk ")]
    public void Step6_PromotionsByPrecedence(string precedence) =>
        Assert.Equal(MessageCategory.Promotions, Classify("anna@example.org", "Hallo", H("Precedence", precedence)));

    [Fact]
    public void Step6_PromotionsByLocalPart()
    {
        Assert.Equal(MessageCategory.Promotions, Classify("newsletter@example.org"));
        Assert.Equal(MessageCategory.Promotions, Classify("Hello@startup.io"));
        // Nur exakter lokaler Teil, kein Teilstring.
        Assert.Equal(MessageCategory.Primary, Classify("newsletterfan@example.org"));
    }

    [Theory]
    [InlineData("50% OFF everything")]
    [InlineData("Großer Rabatt nur heute")]
    [InlineData("Black Friday startet")]
    [InlineData("Скидки до 70%")]
    [InlineData("Большая распродажа")]
    public void Step6_PromotionsBySubject(string subject) =>
        Assert.Equal(MessageCategory.Promotions, Classify("anna@example.org", subject));

    [Fact]
    public void Step6_PromotionsLocalPartBeatsUpdatesLocalPart() =>
        // "news" ist promotions-localpart → Schritt 6 vor Schritt 7.
        Assert.Equal(MessageCategory.Promotions, Classify("news@bank.de", "Monatsinfo"));

    [Theory]
    [InlineData("noreply@bank.de")]
    [InlineData("No-Reply@service.example")]
    [InlineData("mailer-daemon@mx.example")]
    public void Step7_UpdatesByLocalPart(string from) =>
        Assert.Equal(MessageCategory.Updates, Classify(from, "Information"));

    [Fact]
    public void Step8_PrimaryFallback()
    {
        Assert.Equal(MessageCategory.Primary, Classify("oma@example.de", "Wie geht's?"));
        Assert.Equal(MessageCategory.Primary, Classify("", ""));
    }
}
