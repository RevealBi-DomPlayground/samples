# Adding Dynamic / Ad-Hoc Filters to a Reveal Dashboard

This project demonstrates a dynamic dashboard filter builder using the Reveal BI TypeScript DOM API. It enables users to interactively create and manage dashboard filters through a user-friendly interface, supporting both date-based and data-based filtering capabilities. This application showcases how to integrate Reveal BI's powerful visualization capabilities with custom filter controls, allowing users to select fields from their dashboards and automatically generate appropriate filter types while maintaining a responsive and intuitive user experience.

🎥 **Video Walkthrough**: 

<a href="https://www.youtube.com/watch?v=CyxmOsfEXTU" target="_blank">
  <img src="https://img.youtube.com/vi/CyxmOsfEXTU/0.jpg" alt="Watch the Video" style="width:250px; height:auto;">
</a>



🔗 **Helpful Resources**: 
* **Try Reveal for free**: [https://www.revealbi.io/](https://www.revealbi.io/)
* **Upcoming webinars**: [https://www.revealbi.io/webinars](https://www.revealbi.io/webinars)

📲 **Follow Us**: 
* X: [https://x.com/reveal_bi](https://x.com/reveal_bi)
* Facebook: [/revealbi.io](/revealbi.io)
* LinkedIn: [/revealbi](/revealbi)
* Instagram: [/reveal.bi](/reveal.bi)

🚀 **Key Highlights**: 
* Dynamic field detection and automatic filter type assignment based on data types
* Smart handling of date filters with enforcement of single date filter rule
* Support for multi-select data filters with empty selection capability
* Real-time dashboard updates using Reveal BI's DOM API
* Seamless integration with existing dashboards through dropdown selection
* Interactive field selection with visual indicators for data types
* Error handling with user-friendly notifications and loading states

🔧 **Built With**:
* Reveal BI TypeScript DOM - https://github.com/RevealBi/revealbi-dom
* Reveal BI - https://www.revealbi.io/

<img width="1679" alt="image" src="https://github.com/user-attachments/assets/6b9f9af2-2433-45cf-a78c-2bafc541c03e" />

### Assumptions
1. All widgets in the dashboard in the dashboard are created using the same datasource.  This sample is pulling the last datasource spec and schema in the RDASH file.  If you are using multiple data sources in your dashboard, or if you want to manually create datasources, you can do so using the client-side DOM to create data sources / data source items on the client.
2. This sample is using dashboards from https://acmeanalyticsserver.azurewebsites.net/, this is a demo server, if the HTML page takes a minute to load, it's waking up the server.
3. Dashboards changes will not save, as this is a public demo server. 

### 1. Loading the Application
1. Open your web browser
2. Navigate to the application URL
3. The interface will load with two main sections:
   - Left panel: Controls and field selection
   - Right panel: Dashboard viewer

### 2. Selecting a Dashboard

1. Look for the "Select Dashboard" dropdown in the left panel
2. Click the dropdown to see available dashboards
3. Select your desired dashboard
   - The dashboard will automatically load in the right panel
   - Available fields will populate in the left panel under "Fields"
  
To get the dashboards, I am using a server-side API using the Reveal DOM in a .NET Core app:

```csharp
app.MapGet("/dashboards/names", () =>
{
    try
    {
        string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboards");
        var files = Directory.GetFiles(folderPath, "*.rdash");
            var fileNames = files.Select(file =>
            {
                try
                {
                    return new DashboardNames
                    {
                        DashboardFileName = Path.GetFileNameWithoutExtension(file),
                        DashboardTitle = RdashDocument.Load(file).Title
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error Reading FileData {file}: {ex.Message}");
                    return null;
                }
            }).Where(fileData => fileData != null).ToList();

            return Results.Ok(fileNames);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error Reading Directory : {ex.Message}");
        return Results.Problem("An unexpected error occurred while processing the request.");
    }

}).Produces<IEnumerable<DashboardNames>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError);
```

this endpoint returns the dashboard filename and dashboard title:

```json
[
  {
    "dashboardFileName": "Banking",
    "dashboardTitle": "Banking"
  },
  {
    "dashboardFileName": "Customer Orders Analysis",
    "dashboardTitle": "Orders"
  }
]
```

### 3. Adding Filters

#### Understanding Field Types
Fields are marked with different icons:
- 📅 Calendar icon: Date/DateTime fields
- 123 icon: Number fields
- ABC icon: Text/String fields

#### Date Filter Rules
- Only ONE date filter can be active at a time
- Attempting to add multiple date filters will trigger a warning
- If a date filter exists, new date field selections will be ignored

#### Adding Multiple Filters

1. In the "Fields" section:
   - Check the boxes next to fields you want to add as filters
   - You can select multiple fields at once
2. Click the "Add Filters" button
3. The selected fields will be:
   - Added as filters to the dashboard
   - Disabled in the fields list (to prevent duplicate filters)
4. The dashboard will refresh to show the new filters

### 4. Using the Filters

Once filters are added:
1. They appear at the top of the dashboard
2. For date filters:
   - Click to open the date selection interface
   - Choose from predefined ranges or set custom dates
3. For other filters:
   - Click to see available values
   - Select one or multiple values
   - Use the search box to find specific values
