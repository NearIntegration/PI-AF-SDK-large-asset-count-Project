# Large Asset Count Project - How to Handle Millions of AF Elements Efficiently 

Due to the increasing number of connected devices present in industries, more and more PI Systems are built to support up to millions of AF assets and data streams in recent years. Even though OSIsoft produces a healthy PI software ecosystem, the off-the-shelf PI client applications may not yet work well for some use cases in large-asset-count systems. In these cases custom applications often need to be developed using the PI System software libraries, such as [PI AF SDK](https://techsupport.osisoft.com/Products/Developer-Technologies/PI-AF-SDK/Overview).

The goal of this project is to provide a set of sample code that handles representative large-asset-count use cases with the best practices in coding with PI AF SDK.

## Projects

There are four projects in the solution to demonstrate an end-to-end example. 

1. **FlatStructureBuilder**

  A console application to build a flat AF structure of a given number of leaf elements. There are two AF attributes with PI Point data reference representing mode and value. Also each element contains three static AF attributes showing the IDs of itself, Branch, and SubTree. Both Leaf-Branch and Branch-SubTree have an N-to-1 relationship, so that a hierarchy can be built in the next step. 

2. **HierarchyBuilder**

  A console application to create the Subtree/Branch/Leaf hierarchy in AF using weak references based on the static attribute values in leaf elements. After creating the hierarchy, the application will keep monitoring the changes of leaf elements and update the hierarchy accordingly.

3. **CalculationEngine**

  A console application to perform various calculations based on either historical data or real-time value updates including:
    * Roll-ups
    * Condition Detection
    * Historical Analysis
 
  The calculations utilize two methods for retrieving data, signups with the PI Update Manager for incoming data and historical data retrieval using bulk value calls.

4. **Utilities**

  A class library holding AF helper functions and types.

Technical details can be found on [wiki pages](https://github.com/osisoft/PI-AF-SDK-large-asset-count-Project/wiki).

## How to Run the Sample

After compiling the solution, users should run the sample code following the sequence of 1-2-3 against an existing PI System. It will create a flat structure of leaf elements, build a hierarchy based on leaf elements’ attribute values, and then run various calculations against the resulting AF Database. If the flat structure has already been created by a PI Interface or PI Connector, users can run HierarchyBuilder directly with small modifications on the AF Database.

1. Run FlatStructureBuilder.exe with the following app.config. The total leaf element count will be 100 * 20 * 50 = 100,000. You should modify the settings to match your PI System and intended AF Database size. 

    Note that we use PI Random Simulator Interface to feed data to AF Attributes with PI Point DR in leaf elements. Location4 (or scan class) of those PI points is set to 3 to avoid the conflict with two default scan classes in PI Random Interface, so you may need to add a new scan class to the PI Random Simulator Interface on your PI System. Typically PI Points in large-asset-count systems tend to be slow updating points, a scan class of 15 minutes or larger is recommended. 

  ```
    <?xml version="1.0" encoding="utf-8" ?>
    <configuration>
      <startup>
        <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.5" />
      </startup>
      <appSettings>
        <add key="AF Server" value="MyAFServer"/>
        <add key="AF Database" value="HighAsset_Example_100K"/>
        <add key="PI Data Archive" value="MyPIDataArchive"/>
        <add key="Total SubTrees" value="100"/>
        <add key="Branches per SubTree" value="20"/>
        <add key="Leaves Per Branch" value="50"/>
        <add key="Elements per CheckIn" value="100"/>
        <add key="Use multiple threads" value="true"/>
        <add key="Max degrees of parallelism" value="8"/>
        <add key="Use customized CreateConfig" value="true"/>
      </appSettings>
    </configuration>
  ```

2. Run HierarchyBuilder.exe with the following app.config. You should modify the settings to match the target AF Database. After building the hierarchy, the application will continue running and updating the hierarchy if needed. Press any key to exit.

  ```
    <?xml version="1.0" encoding="utf-8" ?>
    <configuration>
      <startup>
        <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.5" />
      </startup>
      <appSettings>
        <add key="AFDatabasePath" value="\\MyAFServer\HighAsset_Example_100K"/>
        <add key="HierarchyLevels" value="Leaf|Branch|Subtree"/>
      </appSettings>
    </configuration>
  ```

3. Run CalculationEngine.exe with the following app.config. One should modify the settings to match the target AF Database. After historical analysis calculations, the application will continue running and monitoring the mode change in leaf elements. Press any key to exit.

  ```
     <?xml version="1.0" encoding="utf-8" ?>
     <configuration>
       <startup>
         <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.5" />
       </startup>
       <appSettings>
         <add key="AFDatabasePath" value="\\MyAFServer\HighAsset_Example_100K"/>
         <add key="RollupPath" value="Leaf|Branch|Subtree"/>
       </appSettings>
     </configuration>
  ```
  
## Questions and Comments

For questions and comments, please join the discussion on [PI Developers Club](https://pisquare.osisoft.com/docs/DOC-1881).

## PI Software Versions

The code has been fully tested using a PI System containing 1 million leaf elements on [PI Server 2015](https://techsupport.osisoft.com/Troubleshooting/Releases/RL01067)
  * PI Data Archive 2015 (v. 3.4.395.64)
  * PI AF Server / Client 2015 (v. 2.7.0.6937)

## Contributors

  This project is based on the original work from [Jin Huang](https://github.com/jhuang0909) and [Barry Shang](https://github.com/bzshang). We encourage contribution from any PI developers. New use case ideas and code contributions are always welcome.  

## LICENSE

Copyright 2015 OSIsoft, LLC.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
