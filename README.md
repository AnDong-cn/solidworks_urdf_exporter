# SolidWorks to URDF Exporter

This fork adds per-link STEP export alongside STL for SolidWorks 2025 / VS2022. See [build, validation, installation and rollback instructions](STEP-EXPORT.md).

Authored and maintained by [Stephen Brawner](brawner@gmail.com). Past supporters include [PickNik Consulting](https://picknik.ai), Verb Surgical, Open Robotics, and Willow Garage. 

## Latest Release

**SolidWorks 2021**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.6.1

**SolidWorks 2020**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.6.0

**SolidWorks 2019 on 2018 SP 5**

https://github.com/ros/solidworks_urdf_exporter/releases/tag/1.5.1

## SolidWorks Version Requirements

1. The minimum required version of SolidWorks for use with this add-in is 2018 Service Pack 5. SolidWorks 2017 or earlier may work. See [this issue](https://github.com/ros/solidworks_urdf_exporter/issues/73).

## Usage

See the [ROS Wiki](http://wiki.ros.org/sw_urdf_exporter) and associated [tutorials](http://wiki.ros.org/sw_urdf_exporter/Tutorials).

## Development

1. Install Visual Studio 2022 with the components in `.vsconfig`, plus .NET SDK 8 or later for the test runner.
1. Install SolidWorks 2025 and its API tools.
1. Open `SW2URDF.sln` and select `Release|x64`. Normal builds do not register COM and do not require administrator privileges.
1. Run `powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -Test -Installer` to build, validate and package.
1. Follow [STEP-EXPORT.md](STEP-EXPORT.md) for installation, backup and rollback. See [VALIDATION.md](VALIDATION.md) for the local verification results.

## Converting mesh format from 3dxml to dae

Executing the following command will convert the format of the exported mesh from 3DXML to DAE, and rewrite the URDF, allowing you to display colored meshes in visualization tools like RViz:

```bash
pip3 install scikit-robot -U
convert-urdf-mesh <URDF_PATH> --output <OUTPUT_URDF_PATH>
```

### Trouble Shooting

1. `AxImp.exe` error - Check the installation of the .Net Tools. If there is no error, install the Windows 10 SDK.
1. `Resourse.resx` error - Check if `sw2urdf/SW2URDF/Resources.resx` exists and is empty. If empty, delete this file then right click the `SW2URDF` in the Solution Explorer and select `Properties`. Navigate to the Resources tab and click the button to create a new file.
