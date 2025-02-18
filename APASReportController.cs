using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Web.Mvc;
using EHCBrokerServiceAgent;
using System.Collections.Specialized;
using HTMSAdminDashboard.Miscellaneous;
using System.Net;
using Newtonsoft.Json;
using System.Data.SqlClient;
using HTMSAdminDashboard.Areas.V2.Models.APAS;
using Oracle.ManagedDataAccess.Client;
using System.Globalization;

namespace HTMSAdminDashboard.Areas.V2.Controllers
{
    public class APASReportController : Controller
    {
        public ActionResult APASReport()
        {
            if (Session["USER_NAME"] != null)
            {
                APAS model = new APAS();
                model.lstChainage = GetChainage();
                return View("APASReport", model);
            }
            else
            {
                return RedirectToAction("Index", "UserLogin", new { area = "" });
            }
        }

        /// <summary>
        /// Realtime pedestrian popup
        /// </summary>
        /// <returns></returns>
        public JsonResult GetTopPedestrianData()
        {
            var pedestrianData = new List<PedestrianDataModel>();
            try
            {
                string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    string query = "SELECT TOP 1 Chainage_desc, CreatedOn, imgpath FROM tbl_Pedestrian_data ORDER BY CreatedOn DESC";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        connection.Open();
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                pedestrianData.Add(new PedestrianDataModel
                                {
                                    ChainageDesc = reader["Chainage_desc"].ToString(),
                                    CreatedOn = Convert.ToDateTime(reader["CreatedOn"]),
                                    ImgPath = reader["imgpath"].ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(JsonConvert.SerializeObject(pedestrianData), JsonRequestBehavior.AllowGet);
            //return Json(pedestrianData); // Return JSON data directly
        }

        /// <summary>
        /// Get chainage list
        /// </summary>
        /// <returns></returns>
        public List<SelectListItem> GetChainage()
        {
            List<SelectListItem> lstchainage = new List<SelectListItem>();
            EHCBroker objEHCBroker = new EHCBroker();
            NameValueCollection serviceParameters = new NameValueCollection();
            NameValueCollection resultParameters = new NameValueCollection();

            serviceParameters["serviceType"] = "Common";
            serviceParameters["requestType"] = "getDetails";
            serviceParameters["methodIdentifier"] = "GetChainageList";
            serviceParameters["controllerName"] = "EHCBService";
            serviceParameters["id"] = "0";

            TempData["traceLoggingMessageEnd"] += "\r\n Calling the Broker Service.";
            resultParameters = objEHCBroker.GetDataFromService(serviceParameters);
            TempData["traceLoggingMessageEnd"] += "\r\n Returned back from the Service.";

            DataTable dt = new DataTable();
            dt = Common.xmlToDataTable(resultParameters["xmlData"]);
            lstchainage.Add(new SelectListItem { Value = "", Text = "ALL" });

            if (dt != null && dt.Rows.Count > 0)
            {
                foreach (DataRow dr in dt.Select())
                {
                    lstchainage.Add(new SelectListItem
                    {
                        Value = dr["CHAINAGE_ID"].ToString(),
                        Text = dr["CHAINAGE_DESC"].ToString()
                    });
                }
            }
            else
            {
                lstchainage.Add(new SelectListItem { Value = "", Text = "Select" });
            }

            return lstchainage;
        }

        /// <summary>
        /// Hourwise pedestrian data
        /// </summary>                

        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="format"></param>
        /// <param name="chainage"></param>
        /// <returns></returns>
        public ActionResult GET_ChartData_Hourwise(string from, string to, string chainage)
        {
            try
            {
                string connectionString = "User Id=MTHLPLAZA;Password=mthl$123;Data Source=10.20.9.55:1521/ETMS;";
                string query = @"
                SELECT 
        ""Site_Location"", 
        ""Actual_Count"", 
        ""Predicted_Count"", 
        ""Time"", 
        ""Date""
    FROM 
        VEHICLE_PREDICTION
    WHERE 
        ""Date"" BETWEEN TO_DATE(:fromDate, 'DD-MM-YY') AND TO_DATE(:toDate, 'DD-MM-YY')
        AND ""Site_Location"" = :chainage
    ORDER BY 
        ""Date"" ASC, 
        ""Time"" ASC";

                DateTime fromDate = ParseDate(from);
                DateTime toDate = ParseDate(to);

                if (toDate < fromDate)
                {
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest, "Invalid date range: 'To' date cannot be earlier than 'From' date.");
                }

                DataTable resultTable = new DataTable();

                using (OracleConnection connection = new OracleConnection(connectionString))
                {
                    connection.Open();
                    using (OracleCommand command = new OracleCommand(query, connection))
                    {
                        command.Parameters.Add(new OracleParameter("fromDate", OracleDbType.Varchar2)).Value = fromDate.ToString("dd-MM-yy");
                        command.Parameters.Add(new OracleParameter("toDate", OracleDbType.Varchar2)).Value = toDate.ToString("dd-MM-yy");
                        command.Parameters.Add(new OracleParameter("chainage", OracleDbType.Varchar2)).Value = chainage; // Bind the chainage parameter

                        using (OracleDataAdapter adapter = new OracleDataAdapter(command))
                        {
                            adapter.Fill(resultTable);
                        }
                    }
                }

                if (resultTable.Rows.Count > 0)
                {
                    var jsonData = resultTable.AsEnumerable().Select(row => new
                    {
                        SiteLocation = row["Site_Location"].ToString(),
                        ActualCount = row["Actual_Count"] == DBNull.Value ? 0 : Convert.ToInt32(row["Actual_Count"]),
                        PredictedCount = row["Predicted_Count"] == DBNull.Value ? 0 : Convert.ToInt32(row["Predicted_Count"]),
                        Time = row["Time"] == DBNull.Value ? 0 : Convert.ToInt32(row["Time"]) // Ensure Time is treated as a numeric value
                    }).ToList();

                    return Json(jsonData, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    return new HttpStatusCodeResult(HttpStatusCode.NoContent, "No data found for the given parameters.");
                }
            }
            catch (FormatException ex)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                var methodInfo = System.Reflection.MethodBase.GetCurrentMethod();
                string fullName = methodInfo.DeclaringType.FullName + "." + methodInfo.Name;
                ex.Data["MethodName"] = fullName;

                new ErrorLog().DoErrorLogging(ex);

                return new HttpStatusCodeResult(HttpStatusCode.InternalServerError, "An error occurred while processing your request.");
            }
        }


        // Refactored ParseDate method
        private DateTime ParseDate(string dateString)
        {
            string[] formats = new[] {
        "MM/dd/yyyy",
        "dd-MM-yyyy",
        "yyyy-MM-dd",
        "MM/dd/yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
        "MM-dd-yyyy HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss"
    };

            DateTime parsedDate;

            if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
            {
                return parsedDate;
            }

            if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
            {
                return parsedDate;
            }

            throw new FormatException($"Date string '{dateString}' is not in a valid format. Expected formats: {string.Join(", ", formats)}");
        }






        /// <summary>
        /// Daywise pedestrian data
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="format"></param>
        /// <param name="chainage"></param>
        /// <returns></returns>
        public ActionResult GET_ChartData_Daywise(string from, string to, string format, string chainage)
        {
            try
            {
                // Initialize service and result parameters
                EHCBroker objEHCBroker = new EHCBroker();
                NameValueCollection serviceParameters = new NameValueCollection();
                NameValueCollection resultParameters = new NameValueCollection();

                // Set parameters
                serviceParameters["serviceType"] = "Common";
                serviceParameters["requestType"] = "getDetails";
                serviceParameters["methodIdentifier"] = "GET_ChartData_Daywise";
                serviceParameters["controllerName"] = "EHCBService";
                serviceParameters["flag"] = from;
                serviceParameters["limit"] = to;
                serviceParameters["type"] = string.Join(",", format);
                serviceParameters["vehicleClassId"] = chainage;

                resultParameters = objEHCBroker.GetDataFromService(serviceParameters);

                List<DataTable> dataTables = Common.ToObject<List<DataTable>>(resultParameters["xmlDataStore"]);

                if (dataTables != null && dataTables.Any())
                {
                    return Json(JsonConvert.SerializeObject(dataTables), JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                // Log the error
                ErrorLog objErrorLog = new ErrorLog();
                var methodInfo = System.Reflection.MethodBase.GetCurrentMethod();
                string fullName = methodInfo.DeclaringType.FullName + "." + methodInfo.Name;
                ex.Data["MethodName"] = fullName;
                objErrorLog.DoErrorLogging(ex);

                // Return an error response
                return new HttpStatusCodeResult(HttpStatusCode.InternalServerError, "An error occurred while processing your request.");
            }

            // Return an empty result if no data is found
            return new HttpStatusCodeResult(HttpStatusCode.NoContent, "No data found for the given parameters.");
        }

        /// <summary>
        /// Monthwise pedestrian data
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="format"></param>
        /// <param name="chainage"></param>
        /// <returns></returns>
        public ActionResult GET_ChartData_Monthwise(string from, string to, string format, string chainage)
        {
            try
            {
                // Initialize service and result parameters
                EHCBroker objEHCBroker = new EHCBroker();
                NameValueCollection serviceParameters = new NameValueCollection();
                NameValueCollection resultParameters = new NameValueCollection();

                // Set parameters
                serviceParameters["serviceType"] = "Common";
                serviceParameters["requestType"] = "getDetails";
                serviceParameters["methodIdentifier"] = "GET_ChartData_Monthwise";
                serviceParameters["controllerName"] = "EHCBService";
                serviceParameters["customerId"] = from;
                serviceParameters["emailId"] = to;
                serviceParameters["mobileNumber"] = string.Join(",", format);
                serviceParameters["lastName"] = chainage;

                // Fetch data from service
                resultParameters = objEHCBroker.GetDataFromService(serviceParameters);

                // Convert XML data to a list of DataTables
                List<DataTable> dataTables = Common.ToObject<List<DataTable>>(resultParameters["xmlDataStore"]);

                // Check if data is available and return as JSON
                if (dataTables != null && dataTables.Any())
                {
                    return Json(JsonConvert.SerializeObject(dataTables), JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                // Log the error
                ErrorLog objErrorLog = new ErrorLog();
                var methodInfo = System.Reflection.MethodBase.GetCurrentMethod();
                string fullName = methodInfo.DeclaringType.FullName + "." + methodInfo.Name;
                ex.Data["MethodName"] = fullName;
                objErrorLog.DoErrorLogging(ex);

                // Return an error response
                return new HttpStatusCodeResult(HttpStatusCode.InternalServerError, "An error occurred while processing your request.");
            }

            // Return an empty result if no data is found
            return new HttpStatusCodeResult(HttpStatusCode.NoContent, "No data found for the given parameters.");
        }
    }
}
