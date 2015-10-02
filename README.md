# Large Asset Count Project - How to Handle Millions of AF Elements Efficiently 

Due to the increasing number of connected devices present in industries, more and more PI Systems were built to support up to millions of AF assets and data streams in recent years. Even though OSIsoft produces a healthy PI software ecosystem, the off-the-shelf PI client applications may not yet work well for some use cases in large-asset-count systems. In these cases custom applications often need to be developed using the PI System software libraries, such as PI AF SDK.

The goal of this project is to provide a set of sample code that handles representative large-asset-count use cases with the best practices in coding with PI AF SDK.

## Projects

There are four projects in the solution to demonstrate an end-to-end example. Users should run the sample code following the sequence of 1-2-3 against an existing PI System. It will create a flat structure of leaf elements, build a hierarchy based on leaf elements’ attribute values, and then run various calculations against the resulting AF Database. 

1. **FlatStructureBuilder**

  A console application to build a flat AF structure of a given number of leaf elements. There are two AF attributes with PI Point data reference representing mode and value. Also each element contains three static AF attributes showing the IDs of itself, branch, and subtree. Both Leaf-Branch and Branch-Subtree have an N-to-1 relationship, so that a hierarchy can be built in the next step. 

2. **HierarchyBuilder**

  A console application to create the Subtree/Branch/Leaf hierarchy in AF using weak references based on the static attribute values in leaf elements. After creating the hierarchy, the application will continue to run and update the hierarchy based on AF changes.

3. **CalculationEngine**

  A console application to perform various calculations based on either historical data or real-time value updates including:
    * Roll-ups
    * Condition Detection
    * Historical Analysis
 
  The calculations utilize two methods for retrieving data, signups with the PI Update Manager for incoming data and historical data retrieval using bulk value calls.

4. **Utilities**

  A class library holding AF helper functions and types.

Technical details can be found on [wiki pages](https://github.com/osisoft/PI-AF-SDK-large-asset-count-Project/wiki).

## PI Software Versions

The code has been fully tested using a PI System containing 1 million leaf elements on [PI Server 2015](https://techsupport.osisoft.com/Troubleshooting/Releases/RL01067)
  * PI Data Archive 2015 (v. 3.4.395.64)
  * PI AF Server / Client 2015 (v. 2.7.0.6937)

## Contributors

  This project is based on the original work from [Jin Huang](https://github.com/jhuang0909) and [Barry Zhang](https://github.com/bzshang). We encourage contribution from any PI developers. New use case ideas and code contributions are always welcome.  

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
