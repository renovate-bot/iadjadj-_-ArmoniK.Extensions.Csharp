// This file is part of the ArmoniK project
// 
// Copyright (C) ANEO, 2021-2021. All rights reserved.
//   W. Kirschenmann   <wkirschenmann@aneo.fr>
//   J. Gurhem         <jgurhem@aneo.fr>
//   D. Dubuc          <ddubuc@aneo.fr>
//   L. Ziane Khodja   <lzianekhodja@aneo.fr>
//   F. Lemaitre       <flemaitre@aneo.fr>
//   S. Djebbar        <sdjebbar@aneo.fr>
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

using ArmoniK.DevelopmentKit.WorkerApi.Common.Exceptions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArmoniK.DevelopmentKit.WorkerApi.Common
{
  public class AppsLoader
  {
    private Assembly assembly_;

    private readonly AssemblyLoadContext loadContext_;
    private readonly Assembly            assemblyGridWorker_;

    private readonly EngineType engineType_;

    private string ArmoniKDevelopmentKitServerApi { get; set; }

    public AppsLoader(IConfiguration configuration, string engineTypeAssemblyName, string pathToZip)
    {
      engineType_ = EngineTypeHelper.ToEnum(engineTypeAssemblyName);

      PathToZip = pathToZip;

      ArmoniKDevelopmentKitServerApi = new EngineTypes()[engineType_];

      var logger = LoggerFactory.Create(builder =>
                                          builder.AddConfiguration(configuration)).CreateLogger<AppsLoader>();

      // Create a new context and mark it as 'collectible'.
      var tempLoadContextName = Guid.NewGuid().ToString();

      loadContext_ = new AssemblyLoadContext(tempLoadContextName,
                                             true);

      if (!ZipArchiver.ArchiveAlreadyExtracted(PathToZip))
        ZipArchiver.UnzipArchive(PathToZip);

      var localPathToAssembly = ZipArchiver.GetLocalPathToAssembly(PathToZip);

      assembly_ = loadContext_.LoadFromAssemblyPath(localPathToAssembly);

      if (assembly_ == null)
      {
        logger.LogError($"Cannot load assembly from path [${localPathToAssembly}]");
        throw new WorkerApiException($"Cannot load assembly from path [${localPathToAssembly}]");
      }

      PathToAssembly = localPathToAssembly;

      var localPathToAssemblyGridWorker = $"{Path.GetDirectoryName(localPathToAssembly)}/{ArmoniKDevelopmentKitServerApi}.dll";

      assemblyGridWorker_ = loadContext_.LoadFromAssemblyPath(localPathToAssemblyGridWorker);

      if (assemblyGridWorker_ == null)
      {
        logger.LogError($"Cannot load assembly from path [${localPathToAssemblyGridWorker}]");
        throw new WorkerApiException($"Cannot load assembly from path [${localPathToAssemblyGridWorker}]");
      }

      logger.LogInformation($"GridWorker assembly from path [${localPathToAssemblyGridWorker}]");

      PathToAssemblyGridWorker = localPathToAssemblyGridWorker;

      var currentDomain = AppDomain.CurrentDomain;
      currentDomain.AssemblyResolve += new(LoadFromSameFolder);

      Assembly LoadFromSameFolder(object sender, ResolveEventArgs args)
      {
        var folderPath = Path.GetDirectoryName(PathToAssembly);
        var assemblyPath = Path.Combine(folderPath ?? "",
                                        new AssemblyName(args.Name).Name + ".dll");

        Assembly assembly;
        try
        {
          assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception)
        {
          folderPath = "/app";
          assemblyPath = Path.Combine(folderPath,
                                      new AssemblyName(args.Name).Name + ".dll");

          if (!File.Exists(assemblyPath)) return null;

          assembly = Assembly.LoadFrom(assemblyPath);
        }

        return assembly;
      }
    }

    public IConfiguration Configuration { get; }

    public string PathToZip { get; set; }

    public string PathToAssembly { get; set; }

    public string PathToAssemblyGridWorker { get; set; }

    public IGridWorker GetGridWorkerInstance()
    {
      // Create an instance of a class from the assembly.
      try
      {
        var classType = assemblyGridWorker_.GetType($"{ArmoniKDevelopmentKitServerApi}.GridWorker");

        if (classType != null)
        {
          var gridworker = (IGridWorker)Activator.CreateInstance(classType);

          return gridworker;
        }
      }
      catch (Exception e)
      {
        Console.WriteLine(e);
        throw new WorkerApiException(e);
      }

      throw new NullReferenceException($"Cannot find ServiceContainer named : {ArmoniKDevelopmentKitServerApi}.GridWorker in dll [{PathToAssemblyGridWorker}]");
    }

    public T GetServiceContainerInstance<T>(string appNamespace, string serviceContainerClassName)
    {
      // Create an instance of a class from the assembly.
      var classType = assembly_.GetType($"{appNamespace}.{serviceContainerClassName}");

      if (classType != null)
      {
        var serviceContainer = (T)Activator.CreateInstance(classType);

        return serviceContainer;
      }

      Dispose();
      throw new NullReferenceException($"Cannot find ServiceContainer named : {appNamespace}.{serviceContainerClassName} in dll [{PathToAssembly}]");
    }

    public void Dispose()
    {
      assembly_ = null;
      if (loadContext_ != null)
        // Unload the context.
        loadContext_.Unload();
    }

    ~AppsLoader()
    {
      Dispose();
    }

    public bool RequestNewAssembly(string engineType, string pathToZipFile)
    {
      if (pathToZipFile == null) throw new ArgumentNullException("pathToZipFile is a null argument");

      if (engineType == null ||
          engineType_ != EngineTypeHelper.ToEnum(engineType) ||
          PathToZip == null ||
          !pathToZipFile.Equals(PathToZip))
        return true;

      return false;
    }
  }
}