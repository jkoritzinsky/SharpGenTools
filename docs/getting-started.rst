####################################
Getting Started with SharpGenTools
####################################

Installing SharpGenTools
========================

To use SharpGenTools, you'll want to install the SDK package, as well as the Runtime package. The SDK package provides MSBuild tooling, and the runtime package provides the required support classes for the code generated from the SDK. You can use the following commands from the .NET CLI to install them:

.. code-block:: bash

   dotnet add package SharpGenTools.Sdk
   dotnet add package SharpGen.Runtime

If you want the support classes for COM libraries, you should also install the ``SharpGen.Runtime.COM`` package.

.. note::

   These packages are separate so advanced and legacy users, specifically the SharpDX project, can use their own runtime support classes. In nearly all cases, you will want to reference both projects.

Making a Basic Mapping File
============================

To generate C# using SharpGenTools, you have to create a mapping file.

Create a file named ``Mapping.xml`` with the following content:

.. code-block:: xml

    <?xml version="1.0" encoding="utf-8"?>
    <config id="MyMapping" xmlns="urn:SharpGen.Config">
        <assembly>MyAssembly</assembly>
        <namespace>MyAssembly.Namespace</namespace>
        <depends>SharpGen.Runtime</depends>
    </config>

Let's go through each of these elements.

  * The ``config`` tag: This is the root tag for your configuration file. The ``id`` attribute uniquely identifes your config file during your build.
  * The ``assembly`` tag: Identifies what assembly for which you are generating code.
  * The ``namespace`` tag: Identifies the root namespace for code generated from this config file.
  * The ``depends`` tag: Declares dependencies on other config files. Optional, but can help ensure that all dependencies are correctly loaded. 

Specifying Include Directories
----------------------------------

Now that we have a mapping file, we can start declaring what C++ files to map.

To include a C++ file, we need to specify our include directories. There are two ways to specify include directories.

    * The ``<sdk>`` element:
       * The ``<sdk>`` element can be used to automatically include common libraries, such as the C++ Standard Library and the Windows SDK. Some examples are below:
       
       .. code-block:: xml

            <sdk name="StdLib" />
            <sdk name="WindowsSdk" version="10.0.15063.0" />
        
    * The ``<include-dir>`` element:
        * The ``<include-dir>`` element allows you to specify a specific directory. Additionally, you can use the ``$(THIS_CONFIG_PATH)`` variable to refer to headers relative to the path to the config file.
        * The ``override`` member can be used to treat the headers as user headers instead of system headers.

        .. code-block:: xml

            <include-dir override="true">$(THIS_CONFIG_PATH)/../path/to/headers</include-dir>


Including a C++ Header
------------------------
Now that we've specified our include directories, we can actually include C++ headers in our code generation.

To include a header, we use the ``<include>`` tag. Below are a few examples:

.. code-block:: xml

    <include file="header.h" namespace="MyAssembly.MyNamespace" attach="true" />
    <include file="header1.h" namespace="MyAssembly.MyNamespace.SubNamespace" output="SubNamespace">
        <attach>MyType</attach>
        <attach>MyType2</attach>
        <attach>MyFunction</attach>
    </include>

The ``file`` attribute specifies which file to include, and the ``namespace`` attribute specifies which namespace the C# elements generated from the C++ in this file should go in. The ``output`` attribute specifies what folder this include's namespace is output to. The ``output`` attribute has to be supplied on at least one ``<include>`` element for each sub-namespace. If it is applied multiple times, the last value takes effect.

Attaching Includes
~~~~~~~~~~~~~~~~~~~

You may have noticed above the ``attach`` attribute and the ``<attach>`` elements. These elements specify what C++ elements to actually generate C# interop for. If the ``attach`` attribute is set to ``true``, all C++ elements in that include that SharpGenTools can map will be mapped. Alternatively, you can use ``<attach>`` elements in the include element to specify specific C++ elements to map. If neither is specified, no code is generated for any of the elements defined in that header. This allows you to specify headers needed for compilation even though they may not be needed for the mapping itself.

.. warning::

    For the both the ``attach`` attribute and the ``<attach>`` element, the C++ elements must be directly defined in that include file.
    
    Additionally, the name in the ``file`` attribute must match case with the first time the header was included, even if the header was first included transitively via a different header. If they don't match, the elements in the header will not be attached to the model.

Adding the Mapping File To the Build
======================================

Now that we have a basic mapping file, all we need to do is add it to the build!

In your ``.csproj`` file, add the line below:

.. code-block:: xml

    <SharpGenMapping Include="path/to/Mapping.xml" />

SharpGenTools will now pick up your mapping file and generate C# for the C++ your config file specifies using the default mappings.

.. note::

    The default mapping does not support mapping free functions. To map free functions, see the :doc:`/custom-mapping` tutorial.
