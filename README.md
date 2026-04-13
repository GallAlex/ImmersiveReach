# ImmersiveReach

<div align="center">

**Immersive Workspace for Accessibility & Reachability Calculations**


[![Unity](https://img.shields.io/badge/Unity-6000.3.2f1-blue.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-HTC_Vive_Pro_2-red.svg)](https://www.vive.com/eu/product/vive-pro2/overview/)
[![OpenXR](https://img.shields.io/badge/OpenXR-VIVE_XR-orange.svg)](https://developer.vive.com/resources/openxr/unity/overview/)
</div>

## Overview

**ImmersiveReach** is a specialized XR framework designed for calculating and visualizing accessibility in complex 3D environments. By providing high-fidelity spatial analysis, the workspace allows researchers to evaluate reachability constraints and plan delicate interactions with sensitive datasets.

While the framework is versatile, its primary application currently focuses on **Preoperative Surgical Planning in Medical Domains.** and **Cultural Heritage and Archaeological Planning**.

## Technical Specifications

| Requirement          | Detail                                                               |
| -------------------- | -------------------------------------------------------------------- |
| **Unity Version** | 6000.3.2f1                                                           |
| **Scripting Backend**| IL2CPP (Windows Build Support)                                       |
| **Graphics API** | Auto Graphics API (DirectX 11/12)                                    |
| **XR Plugin** | VIVE OpenXR Plugin (v2.5.1)                                          |
| **Primary Hardware** | HTC Vive Pro 2                                                       |

## Setup & Installation

1. **Unity Version:** Ensure you are using **Unity 6000.3.2f1**.
2. **Modules:** Install **Windows Build Support (IL2CPP)** via Unity Hub.
3. **XR Configuration:**
   - In **Project Settings > XR Plug-in Manager**, enable **OpenXR** and **VIVE XR Support**.
   - In **OpenXR > Feature Groups**, enable the **VIVE XR - Interaction Group** and **VIVE XR Hand Tracking**.
4. **VIVE Plugin:** Install version **2.5.1** of the VIVE OpenXR Plugin following the [official VIVE installation guide](https://developer.vive.com/resources/openxr/unity/tutorials/setup-and-installation/how-to-install-vive-openxr-plugin/).

---

## Featured Applications

ImmersiveReach serves as the foundation for various research applications:

### 1. Crepuscular Rays for Preoperative Planning in Immersive Environment
The initial version of this framework and its techniques were built upon research established in the following Master Thesis:

> **Maud Andruszak (2025).** *Crepuscular Rays for Preoperative Planning in Immersive Environment.* Master Thesis. University of Passau, Faculty of Computer Science and Mathematics, Chair of Cognitive Sensor Systems.

### 2. Immersive Reachability Planning for the Excavation of Cultural Heritage Objects

> **Alexander Gall, Anja Heim, Laura Longo, Christoph Heinzl** *Immersive Reachability Planning for the Excavation of Cultural Heritage Objects.* AVI2026.


## Contact

Developed and maintained by 
**Alexander Gall**  

Email: [alexander.gall@uni-passau.de](mailto:alexander.gall@uni-passau.de)  
LinkedIn: [LinkedIn Profile](https://www.linkedin.com/in/alexander-gall-1b7039242)  
Website: [Homepage](https://sites.google.com/view/alexandergall/)
