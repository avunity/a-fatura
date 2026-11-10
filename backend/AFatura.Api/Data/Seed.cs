using AFatura.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Data;

public static class Seed
{
    public static async Task RunAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

        // Use migrations when they exist; otherwise create the schema directly (first MVP deploy).
        if (db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync();
        else
        {
            await db.Database.EnsureCreatedAsync();
            await SchemaUpgrades.ApplyAsync(db, log); // databases created by an older version get the new tables/columns
        }

        if (!await db.Categories.AnyAsync(c => c.OrganizationId == null))
        {
            db.Categories.AddRange(DefaultCategories());
            await db.SaveChangesAsync();
            log.LogInformation("Seeded default categories");
        }

        var adminEmail = config["Admin:Email"];
        var adminPassword = config["Admin:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword)
            && !await db.Users.AnyAsync(u => u.Email == adminEmail.ToLowerInvariant()))
        {
            var admin = new User
            {
                Email = adminEmail.ToLowerInvariant(),
                Name = "Backoffice",
                PlatformRole = PlatformRole.Admin
            };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, adminPassword);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            log.LogInformation("Seeded admin user {Email}", admin.Email);
        }
    }

    // VAT deductibility values are informative defaults only; the accountant validates them per client.
    private static IEnumerable<Category> DefaultCategories() =>
    [
        new() { Name = "Combustível", Color = "#ef4444", IsVehicleRelated = true, VatDeductiblePercent = 50,
                Keywords = "combustivel,gasoleo,gasóleo,gasolina,diesel,galp,repsol,bp,cepsa,prio,shell,intermarché combustíveis,abastecimento" },
        new() { Name = "Carregamento Elétrico", Color = "#22c55e", IsVehicleRelated = true, VatDeductiblePercent = 100,
                Keywords = "carregamento,kwh,mobi.e,mobie,miio,powerdot,ionity,tesla supercharger,electricidade veiculo,ev charging" },
        new() { Name = "Portagens e Via Verde", Color = "#f97316", IsVehicleRelated = true,
                Keywords = "portagem,via verde,brisa,ascendi,a25,a1,a2,scut" },
        new() { Name = "Seguro Automóvel", Color = "#a855f7", IsVehicleRelated = true, VatDeductiblePercent = 0,
                Keywords = "seguro automovel,seguro automóvel,apólice,fidelidade,tranquilidade,ageas,allianz,generali,ok! teleseguros,liberty" },
        new() { Name = "Manutenção Viatura", Color = "#eab308", IsVehicleRelated = true,
                Keywords = "oficina,pneus,revisão,revisao,inspeção,inspecao,norauto,midas,carglass,lavagem" },
        new() { Name = "Estacionamento", Color = "#06b6d4", IsVehicleRelated = true,
                Keywords = "estacionamento,parque,emel,saba,empark,telpark" },
        new() { Name = "Equipamento Informático", Color = "#3b82f6", VatDeductiblePercent = 100,
                Keywords = "computador,portatil,portátil,monitor,teclado,rato,worten,fnac,pcdiga,globaldata,chip7,apple,informática,informatica" },
        new() { Name = "Software e Subscrições", Color = "#6366f1", VatDeductiblePercent = 100,
                Keywords = "software,subscrição,subscricao,licença,licenca,microsoft,google workspace,adobe,github,aws,azure,openai,anthropic,saas" },
        new() { Name = "Telecomunicações", Color = "#0ea5e9", VatDeductiblePercent = 100,
                Keywords = "meo,nos,vodafone,nowo,digi,internet,telemóvel,telemovel,fibra" },
        new() { Name = "Restauração", Color = "#f43f5e", VatDeductiblePercent = 0,
                Keywords = "restaurante,café,cafe,pastelaria,snack,refeição,refeicao,almoço,jantar,uber eats,glovo,bolt food" },
        new() { Name = "Alojamento", Color = "#ec4899",
                Keywords = "hotel,alojamento,hostel,booking,airbnb,pousada" },
        new() { Name = "Transportes e Deslocações", Color = "#14b8a6",
                Keywords = "cp comboios,comboio,metro,carris,uber,bolt,táxi,taxi,tap,ryanair,easyjet,rede expressos,flixbus" },
        new() { Name = "Material de Escritório", Color = "#84cc16", VatDeductiblePercent = 100,
                Keywords = "papelaria,staples,material escritório,material escritorio,toner,papel" },
        new() { Name = "Renda e Condomínio", Color = "#78716c",
                Keywords = "renda,arrendamento,condomínio,condominio" },
        new() { Name = "Água, Luz e Gás", Color = "#facc15",
                Keywords = "edp,endesa,goldenergy,iberdrola,galp energia,epal,águas,aguas,gás,gas natural" },
        new() { Name = "Serviços Profissionais", Color = "#8b5cf6", VatDeductiblePercent = 100,
                Keywords = "contabilidade,consultoria,advogado,honorários,honorarios,serviços,servicos" },
        new() { Name = "Formação", Color = "#10b981",
                Keywords = "formação,formacao,curso,workshop,conferência,conferencia,udemy,coursera" },
        new() { Name = "Outros", Color = "#94a3b8", Keywords = "" },
    ];
}
