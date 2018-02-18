﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CodeConverter.VsExtension
{
    static class VisualStudioInteraction
    {
        public static VsDocument GetSingleSelectedItemOrDefault()
        {
            var monitorSelection = Package.GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            var solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;

            if ((monitorSelection == null) || (solution == null))
                return null;

            IntPtr hierarchyPtr = IntPtr.Zero;
            IntPtr selectionContainerPtr = IntPtr.Zero;

            try {
                var hresult = monitorSelection.GetCurrentSelection(out hierarchyPtr, out uint itemID, out var multiItemSelect, out selectionContainerPtr);
                if (ErrorHandler.Failed(hresult) || (hierarchyPtr == IntPtr.Zero) || (itemID == VSConstants.VSITEMID_NIL))
                    return null;

                if (multiItemSelect != null)
                    return null;

                if (itemID == VSConstants.VSITEMID_ROOT)
                    return null;

                var hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                if (hierarchy == null)
                    return null;

                Guid guidProjectID = Guid.Empty;

                if (ErrorHandler.Failed(solution.GetGuidOfProject(hierarchy, out guidProjectID)))
                    return null;

                return new VsDocument((IVsProject) hierarchy, guidProjectID, itemID);
            } finally {
                if (selectionContainerPtr != IntPtr.Zero) {
                    Marshal.Release(selectionContainerPtr);
                }

                if (hierarchyPtr != IntPtr.Zero) {
                    Marshal.Release(hierarchyPtr);
                }
            }
        }
        private static IEnumerable<T> GetSelectedSolutionExplorerItems<T>() where T: class
        {
            var selectedObjects = (IEnumerable<object>) Dte.ToolWindows.SolutionExplorer.SelectedItems;
            var selectedItems = selectedObjects.Cast<UIHierarchyItem>().ToList();

            var returnType = typeof(T);
            return selectedItems.Select(item => item.Object).Where(returnType.IsInstanceOfType).Cast<T>();
        }

        private static DTE2 Dte => Package.GetGlobalService(typeof(DTE)) as DTE2;


        public static IReadOnlyCollection<Project> GetSelectedProjects(string projectExtension)
        {
            var items = GetSelectedSolutionExplorerItems<Solution>()
                .SelectMany(s => s.Projects.Cast<Project>())
                .Concat(GetSelectedSolutionExplorerItems<Project>())
                .Concat(GetSelectedSolutionExplorerItems<ProjectItem>().Where(p => p.SubProject != null).Select(p => p.SubProject))
                .Where(project => project.FullName.EndsWith(projectExtension, StringComparison.InvariantCultureIgnoreCase))
                .ToList();
            return items;
        }

        public static IWpfTextViewHost GetCurrentViewHost(IServiceProvider serviceProvider)
        {
            IVsTextManager txtMgr = (IVsTextManager)serviceProvider.GetService(typeof(SVsTextManager));
            IVsTextView vTextView = null;
            int mustHaveFocus = 1;
            txtMgr.GetActiveView(mustHaveFocus, null, out vTextView);
            IVsUserData userData = vTextView as IVsUserData;
            if (userData == null)
                return null;

            object holder;
            Guid guidViewHost = DefGuidList.guidIWpfTextViewHost;
            userData.GetData(ref guidViewHost, out holder);

            return holder as IWpfTextViewHost;
        }

        public static ITextDocument GetTextDocument(this IWpfTextViewHost viewHost)
        {
            ITextDocument textDocument = null;
            viewHost.TextView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out textDocument);
            return textDocument;
        }

        public static VisualStudioWorkspace GetWorkspace(IServiceProvider serviceProvider)
        {
            return (VisualStudioWorkspace) serviceProvider.GetService(typeof(VisualStudioWorkspace)); 
        }

        public static void ShowException(IServiceProvider serviceProvider, string title, Exception ex)
        {
            VsShellUtilities.ShowMessageBox(
                serviceProvider,
                $"An error has occured during conversion: {ex}",
                title,
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>
        /// Identifies the internal object types.
        /// </summary>
        /// <param name="item">The item.</param>
        [Conditional("DEBUG")]
        public static void IdentifyInternalObjectTypes(UIHierarchyItem item)
        {
            if (item == null) {
                Debug.WriteLine("No item provided.");

                return;
            }

            if (item.Object == null) {
                Debug.WriteLine("No item object is available.");

                return;
            }

            // Loop through all the assemblies in the current app domain
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            // Loop through each assembly
            for (Int32 index = 0; index < loadedAssemblies.Length; index++) {
                // Assume that the assembly to check against is EnvDTE.dll
                IdentifyInternalObjectTypes(item, loadedAssemblies[index]);
            }
        }

        /// <summary>
        /// Identifies the internal object types.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="assemblyToCheck">The assembly to check.</param>
        [Conditional("DEBUG")]
        public static void IdentifyInternalObjectTypes(UIHierarchyItem item, Assembly assemblyToCheck)
        {
            try {
                // Get the types that are publically available
                Type[] exportedTypes = assemblyToCheck.GetExportedTypes();

                // Loop through each type
                for (Int32 index = 0; index < exportedTypes.Length; index++) {
                    // Check if the object instance is of this type
                    if (exportedTypes[index].IsInstanceOfType(item.Object)) {
                        Debug.WriteLine(exportedTypes[index].FullName);
                    }
                }
            } catch (Exception e) {
                
            }
        }
    }
}
