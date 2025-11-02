import json
import re

# Read the JSON file
with open('jstradetrackerconsoleapp/ConsoleApp1/feeds_new.json', 'r') as f:
    data = json.load(f)

# Process each feed
for feed in data['feeds']:
    url = feed['url']
    # Check if the URL already has &limit= parameter
    if '&limit=' not in url:
        # Add &limit=3000 to the URL
        feed['url'] = url + '&limit=3000'

# Write back to the file
with open('jstradetrackerconsoleapp/ConsoleApp1/feeds_new.json', 'w') as f:
    json.dump(data, f, indent=2)

print("Successfully added &limit=3000 to all URLs that didn't have it already.")