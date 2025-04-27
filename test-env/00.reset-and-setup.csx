#r "nuget: Lestaly, 0.76.0"
#nullable enable
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(config: c => c.AnyPause(), action: async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    await "dotnet".args("script", ThisSource.RelativeFile("10.reset-restart.csx"), "--", "--no-interact").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("11.init-setup.csx"), "--", "--no-interact").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("12.generate-api-key.csx"), "--", "--no-interact").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("13.create-principals.csx"), "--", "--no-interact").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("14.create-quota.csx"), "--", "--no-interact").echo().result().success();
});
