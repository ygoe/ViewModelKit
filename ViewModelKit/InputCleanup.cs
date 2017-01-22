using System;

namespace ViewModelKit
{
	// TODO: Let the target application define this class, mark it with an attribute for VMK to recognise it

	/// <summary>
	/// Provides methods for cleaning up user input and convert it to a specific format.
	/// </summary>
	public static class InputCleanup
	{
		/// <summary>
		/// Cleans up a user input string for a string type property.
		/// </summary>
		/// <param name="str">The text entered by the user.</param>
		/// <returns>The cleaned string value.</returns>
		public static string CleanupString(string str)
		{
			if (str == null) return null;
			return str.Trim();
		}

		/// <summary>
		/// Cleans up a user input string for an int type property.
		/// </summary>
		/// <param name="str">The text entered by the user.</param>
		/// <returns>The cleaned string value.</returns>
		public static string CleanupInt(string str)
		{
			if (str == null) return null;
			str = str.Trim();
			if (str.Length == 0) return "";
			try
			{
				long l = Convert.ToInt64(str);
				return Convert.ToString(l);
			}
			catch
			{
				return str;
			}
		}

		/// <summary>
		/// Cleans up a user input string for a double type property.
		/// </summary>
		/// <param name="str">The text entered by the user.</param>
		/// <returns>The cleaned string value.</returns>
		public static string CleanupDouble(string str)
		{
			if (str == null) return null;
			str = str.Trim();
			if (str.Length == 0) return "";
			try
			{
				double d = Convert.ToDouble(str);
				return Convert.ToString(d);
			}
			catch
			{
				return str;
			}
		}

		/// <summary>
		/// Cleans up a user input string for a local date value.
		/// </summary>
		/// <param name="str">The text entered by the user.</param>
		/// <returns>The cleaned string value.</returns>
		public static string CleanupDate(string str)
		{
			if (str == null) return null;
			str = str.Trim();
			if (str.Length == 0) return "";
			try
			{
				DateTime d = Convert.ToDateTime(str);
				return d.ToShortDateString();
			}
			catch
			{
				return str;
			}
		}

		/// <summary>
		/// Cleans up a user input string for an ISO date value.
		/// </summary>
		/// <param name="str">The text entered by the user.</param>
		/// <returns>The cleaned string value.</returns>
		public static string CleanupIsoDate(string str)
		{
			if (str == null) return null;
			str = str.Trim();
			if (str.Length == 0) return "";
			try
			{
				DateTime d = Convert.ToDateTime(str);
				return d.ToString("yyyy-MM-dd");
			}
			catch
			{
				return str;
			}
		}
	}
}
