# RevitAI

Autodesk Revit plugin project organized into multiple solution files that target versions 2023 - 2027.

## Table of content

<!-- TOC -->
* [Prerequisites](#prerequisites)
* [Solution Structure](#solution-structure)
* [Project Structure](#project-structure)
* [Building](#building)
  * [Building the MSI installer and the Autodesk bundle on local machine](#building-the-msi-installer-and-the-autodesk-bundle-on-local-machine)
* [Conditional compilation for a specific Revit version](#conditional-compilation-for-a-specific-revit-version)
* [Managing Supported Revit Versions](#managing-supported-revit-versions)
  * [Solution configurations](#solution-configurations)
  * [Project configurations](#project-configurations)
* [API references](#api-references)
* [Learn More](#learn-more)
<!-- TOC -->

## Prerequisites

Before you can build this project, you need to install .NET and IDE.
If you haven't already installed these, you can do so by visiting the following:

- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet)
- [JetBrains Rider](https://www.jetbrains.com/rider/) or [Visual Studio](https://visualstudio.microsoft.com/)

## Solution Structure

| Folder  | Description                                                                |
|---------|----------------------------------------------------------------------------|
| build   | ModularPipelines build system. Used to automate project builds             |
| install | Add-in installer, called implicitly by the ModularPipelines build          |
| source  | Project source code folder. Contains all solution projects                 |
| output  | Folder of generated files by the build system, such as bundles, installers |

## Project Structure

| Folder     | Description                                                                                                                                                                                          |
|------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Commands   | External commands invoked from the Revit ribbon. Registered in the `Application` class                                                                                                               |
| Models     | Classes that encapsulate the app's data, include data transfer objects (DTOs). More [details](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm).                                      |
| ViewModels | Classes that implement properties and commands to which the view can bind data. More [details](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm).                                     |
| Views      | Classes that are responsible for defining the structure, layout and appearance of what the user sees on the screen. More [details](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm). |
| Resources  | Images, sounds, localisation files, etc.                                                                                                                                                             |
| Utils      | Utilities, extensions, helpers used across the application                                                                                                                                           |

## Building

We recommend JetBrains Rider as preferred IDE, since it has outstanding .NET support. If you don't have Rider installed, you can download it
from [here](https://www.jetbrains.com/rider/).

1. Open JetBrains Rider
2. In the `Solutions Configuration` drop-down menu, select `Release.R27` or `Debug.R27`. Suffix `R27` means compiling for the Revit 2027.
3. After the solution loads, you can build it by clicking on `Build -> Build Solution`.
4. `Debug` button will start Revit add-in in the debug mode.

   ![image](https://github.com/user-attachments/assets/d209d863-a6d5-43a9-83e1-5eeb2b9fddac)

Also, you can use Visual Studio. If you don't have Visual Studio installed, download it from [here](https://visualstudio.microsoft.com/downloads/).

1. Open Visual Studio
2. In the `Solutions Configuration` drop-down menu, select `Release.R27` or `Debug.R27`. Suffix `R27` means compiling for the Revit 2027.
3. After the solution loads, you can build it by clicking on `Build -> Build Solution`.

### Building the MSI installer and the Autodesk bundle on local machine

To build the project for all versions, create the installer and bundle, this project uses [ModularPipelines](https://github.com/thomhurst/ModularPipelines)

To execute your ModularPipelines build locally, you can follow these steps:

1. **Navigate to your project directory**. Open a terminal / command prompt and navigate to your project's root directory.
2. **Run the build**. Once you have navigated to your project's root directory, you can run the ModularPipelines build by calling:

   Compile:
   ```shell
   cd build; dotnet run
   ```

   Create installer and bundle:
   ```shell
   cd build; dotnet run -- pack
   ```

   This command will execute the ModularPipelines build defined in your project.

## Conditional compilation for a specific Revit version

To write code compatible with different Revit versions, use the directives **#if**, **#elif**, **#else**, **#endif**.

```c#
#if REVIT2027
    //Your code here
#endif
```

To target a specific Revit version, set the solution configuration in your IDE interface to match that version.
E.g., select the `Debug.R27` configuration for the Revit 2027 API.

The project has available constants such as `REVIT2027`, `REVIT2027_OR_GREATER`. 
Create conditions, experiment to achieve the desired result.

> For generating directives, a Revit MSBuild SDK is used.
> You can find more detailed documentation about it here: [Revit MSBuild SDK](https://github.com/Nice3point/Revit.Build.Tasks)

To support the latest APIs in legacy Revit versions:

```c#
#if REVIT2021_OR_GREATER
    UnitUtils.ConvertFromInternalUnits(69, UnitTypeId.Millimeters);
#else
    UnitUtils.ConvertFromInternalUnits(69, DisplayUnitType.DUT_MILLIMETERS);
#endif
```

`#if REVIT2021_OR_GREATER` сompiles a block of code for Revit versions 21, 22, 23 and greater.

To support removed APIs in newer versions of Revit, you can invert the constant:

```c#
#if !REVIT2023_OR_GREATER
    var builtinCategory = (BuiltInCategory) category.Id.IntegerValue;
#endif
```

`#if !REVIT2023_OR_GREATER` сompiles a block of code for Revit versions 22, 21, 20 and lower.

## Managing Supported Revit Versions

To extend or reduce the range of supported Revit API versions, you need to update the solution and project configurations.

### Solution configurations

Solution configurations determine which projects are built and how they are configured.

To support multiple Revit versions:
- Open the `.sln` file.
- Add or remove configurations for each Revit version.

Example:

```text
GlobalSection(SolutionConfigurationPlatforms) = preSolution
    Debug.R25|Any CPU = Debug.R25|Any CPU
    Debug.R26|Any CPU = Debug.R26|Any CPU
    Debug.R27|Any CPU = Debug.R27|Any CPU
    Release.R25|Any CPU = Release.R25|Any CPU
    Release.R26|Any CPU = Release.R26|Any CPU
    Release.R27|Any CPU = Release.R27|Any CPU
EndGlobalSection
```

For example `Debug.R27` is the Debug configuration for Revit 2027 version.

> If you are just ending maintenance for some version, removing the Solution configurations without modifying the Project configurations is enough.

### Project configurations

Project configurations define build conditions for specific versions.

To add or remove support:
- Open `.csproj` file
- Add or remove configurations for Debug and Release builds.

Example:

```xml
<PropertyGroup>
    <Configurations>Debug.R25;Debug.R26;Debug.R27</Configurations>
    <Configurations>$(Configurations);Release.R25;Release.R26;Release.R27</Configurations>
</PropertyGroup>
```

> Edit the `.csproj` file only manually, IDEs often break configurations.

Revit MSBuild SDK automatically sets the required `TargetFramework` based on the `RevitVersion`, extracted from the solution configuration name. 

If you need to add support for an unreleased or unsupported version of Revit that the SDK doesn't yet know about, you can add a conditional block to specify the `TargetFramework` manually:

```xml
<PropertyGroup>
    <TargetFramework Condition="$(RevitVersion) == '2027'">net10.0-windows7.0</TargetFramework>
</PropertyGroup>
```

## API references

To support CI/CD pipelines and build a project for Revit versions not installed on your computer, use Nuget packages.

> Revit API dependencies are available in the [Revit.API](https://github.com/Nice3point/RevitApi) repository.

The Nuget package version must include wildcards `Version="$(RevitVersion).*"` to automatically include adding a specific package version, depending on the selected solution configuration.

```xml
<ItemGroup>
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="$(RevitVersion).*"/>
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="$(RevitVersion).*"/>
</ItemGroup>
```

## Learn More

* You can explore more on the [RevitTemplates Wiki](https://github.com/Nice3point/RevitTemplates/wiki) page.
